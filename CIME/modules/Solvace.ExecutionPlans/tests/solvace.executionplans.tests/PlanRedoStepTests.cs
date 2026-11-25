using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 78794 (#1077): a infra recusou o script da #1078 (erro 229 em msdb) e o especialista refez o script na conversa. Antes,
/// só os arquivos eram regravados e nada aparecia nas sub-tarefas. Agora a entrega refeita vira sub-tarefa nova, concluída,
/// logo depois da refeita. As que dependiam dela passam a depender também da nova e ganham o aviso na instrução.
/// </summary>
public class PlanRedoStepTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-25T18:00:00Z");

    private static ExecutionStepDefinition Def(string key, string? executor = null, params string[] dependsOn) =>
        new() { Key = key, Title = key, Description = $"instrução de {key}", Executor = executor ?? ExecutionExecutor.Claude, DependsOn = dependsOn.ToList() };

    /// <summary>Correção como a do 78794: script redigido, chamado aberto (aguardando), validação e fechamento pendentes.</summary>
    private static ExecutionPlan Correction()
    {
        var plan = new ExecutionPlan("78794", "analisar-bug", "Correção 78794", null, Guid.NewGuid(), "Alex", Now);
        plan.UpsertSteps([
            Def("preparar-chamado-job-ogden"),
            Def("chamado-job-ogden", ExecutionExecutor.User, "preparar-chamado-job-ogden"),
            Def("validar-chamado-job-ogden", null, "chamado-job-ogden"),
            Def("fechar-card", null, "validar-chamado-job-ogden"),
        ], Now);
        plan.CompleteStep("preparar-chamado-job-ogden", "ok", "claude", Now, true);
        plan.UpdateStep("chamado-job-ogden", ExecutionStatus.Waiting, "Aguardando o chamado 2679", null, null, null, null, "claude", Now,
            ExecutionWaitingOn.External);
        return plan;
    }

    private static string[] Keys(ExecutionPlan plan) => plan.Steps.OrderBy(s => s.Order).Select(s => s.Key).ToArray();

    private static ExecutionStep Step(ExecutionPlan plan, string key) => plan.Steps.Single(s => s.Key == key);

    [Fact]
    public void Redo_enters_right_after_the_redone_step_already_completed()
    {
        var plan = Correction();
        var (step, _) = plan.RedoStep("preparar-chamado-job-ogden", Def("pedido-refazer-script"), "infra recusou: erro 229", "Alex", Now);

        Assert.Equal(["preparar-chamado-job-ogden", "pedido-refazer-script", "chamado-job-ogden", "validar-chamado-job-ogden", "fechar-card"], Keys(plan));
        Assert.Equal(ExecutionStatus.Completed, step.Status);
        Assert.Contains("infra recusou: erro 229", step.StatusReason);
        Assert.Equal(["preparar-chamado-job-ogden"], step.DependsOn);
        Assert.NotNull(step.StartedAt);
        Assert.NotNull(step.FinishedAt);
        // A espera do chamado continua: só a instrução ganha o aviso.
        Assert.Equal(ExecutionStatus.Waiting, Step(plan, "chamado-job-ogden").Status);
    }

    [Fact]
    public void Dependents_are_warned_and_the_direct_ones_depend_on_the_new_step()
    {
        var plan = Correction();
        var (_, warned) = plan.RedoStep("preparar-chamado-job-ogden", Def("pedido-refazer-script"), "infra recusou: erro 229", "Alex", Now);

        Assert.Equal(["chamado-job-ogden", "validar-chamado-job-ogden", "fechar-card"], warned.Select(s => s.Key).ToArray());
        Assert.Equal(["preparar-chamado-job-ogden", "pedido-refazer-script"], Step(plan, "chamado-job-ogden").DependsOn);
        Assert.Equal(["chamado-job-ogden"], Step(plan, "validar-chamado-job-ogden").DependsOn);
        Assert.All(warned, s =>
        {
            Assert.StartsWith("> [!WARNING]", s.Description);
            Assert.Contains("pedido-refazer-script", s.Description);
            Assert.Contains($"instrução de {s.Key}", s.Description);
        });
        Assert.Equal("instrução de preparar-chamado-job-ogden", Step(plan, "preparar-chamado-job-ogden").Description);
    }

    [Fact]
    public void Without_declared_dependents_the_following_open_steps_are_warned()
    {
        var plan = new ExecutionPlan("78794", "analisar-bug", "Correção 78794", null, Guid.NewGuid(), "Alex", Now);
        plan.UpsertSteps([Def("script"), Def("chamado"), Def("fechar-card")], Now);
        plan.CompleteStep("script", "ok", "claude", Now, true);

        var (_, warned) = plan.RedoStep("script", Def("pedido-refazer-script"), "recusado", "Alex", Now);

        Assert.Equal(["chamado", "fechar-card"], warned.Select(s => s.Key).ToArray());
        Assert.Empty(Step(plan, "chamado").DependsOn);
    }

    [Fact]
    public void Mirror_item_shows_the_redo_as_a_completed_task_after_the_redone_one()
    {
        var plan = Correction();
        var item = new WorkItem("78794", WorkItemType.Fix, "Correção 78794", null, WorkItemOrigin.Correction, null, plan.Id,
            plan.Steps.OrderBy(s => s.Order).Select(s => new WorkTaskInput { Title = s.Title, Kind = WorkTaskKind.Manual, StepKey = s.Key }).ToList(),
            Guid.NewGuid(), "Alex", Now);

        plan.RedoStep("preparar-chamado-job-ogden", Def("pedido-refazer-script"), "recusado", "Alex", Now);
        item.AddStepTasks(plan.Steps.Select(s => new WorkTaskInput { Title = s.Title, Kind = WorkTaskKind.Manual, StepKey = s.Key }), Now);
        item.ReorderByStep(plan.Steps.ToDictionary(s => s.Key, s => s.Order));
        foreach (var s in plan.Steps)
        {
            item.SyncStepText(s.Key, s.Title, s.Description, Now);
            item.SyncStep(s.Key, s.Status, s.StatusReason, "Plano", Now);
        }

        Assert.Equal(Keys(plan), item.OrderedTasks.Select(t => t.StepKey).ToArray());
        Assert.Equal(WorkTaskStatus.Completed, item.Tasks.Single(t => t.StepKey == "pedido-refazer-script").Status);
        Assert.StartsWith("> [!WARNING]", item.Tasks.Single(t => t.StepKey == "chamado-job-ogden").Description);
    }

    [Fact]
    public void Only_a_completed_step_can_be_redone_and_the_reason_is_required()
    {
        var plan = Correction();
        Assert.Throws<DomainException>(() => plan.RedoStep("chamado-job-ogden", Def("pedido-x"), "motivo", "Alex", Now));
        Assert.Throws<DomainException>(() => plan.RedoStep("preparar-chamado-job-ogden", Def("pedido-x"), " ", "Alex", Now));
        Assert.Throws<DomainException>(() => plan.RedoStep("preparar-chamado-job-ogden", Def("fechar-card"), "motivo", "Alex", Now));
    }
}
