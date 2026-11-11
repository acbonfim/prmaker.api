using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 78262: "antes de continuar, crie uma sub-tarefa no #744 (item da análise) para verificar os logs de acesso" — o
/// pedido vira etapa nova, pendente, do plano, antes da etapa em espera; as outras etapas não mudam e o item espelho
/// ganha a sub-tarefa na mesma posição.
/// </summary>
public class PlanAddStepTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T12:00:00Z");

    private static ExecutionStepDefinition Def(string key, string? taskKind = null) =>
        new() { Key = key, Title = key, Executor = ExecutionExecutor.Claude, TaskKind = taskKind };

    /// <summary>Análise como a do 78262: tudo concluído até a causa raiz e "propor-solucoes" esperando respostas.</summary>
    private static ExecutionPlan Analysis()
    {
        var plan = new ExecutionPlan("78262", "analisar-bug", "Análise do bug 78262", null, Guid.NewGuid(), "Alex", Now);
        plan.UpsertSteps([Def("coletar-dados"), Def("causa-raiz"), Def("publicar"), Def("propor-solucoes")], Now);
        foreach (var key in new[] { "coletar-dados", "causa-raiz", "publicar" })
            plan.CompleteStep(key, "ok", "claude", Now, true);
        plan.UpdateStep("propor-solucoes", ExecutionStatus.Waiting, "Aguardando respostas", null, null, null, null, "claude", Now,
            ExecutionWaitingOn.Answer);
        return plan;
    }

    private static string[] Keys(ExecutionPlan plan) => plan.Steps.OrderBy(s => s.Order).Select(s => s.Key).ToArray();

    [Fact]
    public void New_step_enters_before_the_waiting_step_and_keeps_the_others()
    {
        var plan = Analysis();
        var step = plan.AddStep(Def("pedido-verificar-logs-de-acesso", "research"), null, Now);

        Assert.Equal(ExecutionStatus.Pending, step.Status);
        Assert.Equal(["coletar-dados", "causa-raiz", "publicar", "pedido-verificar-logs-de-acesso", "propor-solucoes"], Keys(plan));
        Assert.All(plan.Steps.Where(s => s.Key is "coletar-dados" or "causa-raiz" or "publicar"),
            s => Assert.Equal(ExecutionStatus.Completed, s.Status));
        Assert.Equal(ExecutionStatus.Waiting, plan.Steps.Single(s => s.Key == "propor-solucoes").Status);
    }

    [Fact]
    public void Mirror_item_gains_the_task_in_the_same_position()
    {
        var plan = Analysis();
        var item = new WorkItem("78262", WorkItemType.Analysis, "Análise inicial do bug #78262", null, WorkItemOrigin.Analysis, null, plan.Id,
            plan.Steps.OrderBy(s => s.Order).Select(s => new WorkTaskInput { Title = s.Title, Kind = WorkTaskKind.Manual, StepKey = s.Key }).ToList(),
            Guid.NewGuid(), "Alex", Now);

        plan.AddStep(Def("pedido-verificar-logs"), null, Now);
        var added = item.AddStepTasks(plan.Steps.Select(s => new WorkTaskInput { Title = s.Title, Kind = WorkTaskKind.Manual, StepKey = s.Key }), Now);
        item.ReorderByStep(plan.Steps.ToDictionary(s => s.Key, s => s.Order));

        Assert.Single(added);
        Assert.True(item.MirrorsPlan);
        Assert.Equal(Keys(plan), item.OrderedTasks.Select(t => t.StepKey).ToArray());
    }

    [Fact]
    public void Before_points_to_an_unfinished_step()
    {
        var plan = Analysis();
        Assert.Throws<DomainException>(() => plan.AddStep(Def("pedido-x"), "causa-raiz", Now));
        Assert.Throws<DomainException>(() => plan.AddStep(Def("pedido-x"), "nao-existe", Now));

        plan.AddStep(Def("pedido-x"), "propor-solucoes", Now);
        Assert.Equal("pedido-x", Keys(plan)[3]);
    }

    [Fact]
    public void Existing_key_is_refused()
    {
        var plan = Analysis();
        Assert.Throws<DomainException>(() => plan.AddStep(Def("causa-raiz"), null, Now));
    }

    [Fact]
    public void All_steps_finished_goes_to_the_end_and_finished_plan_refuses()
    {
        var plan = Analysis();
        plan.CompleteStep("propor-solucoes", "ok", "claude", Now, true);
        plan.AddStep(Def("pedido-x"), null, Now);
        Assert.Equal("pedido-x", Keys(plan)[^1]);

        var cancelled = Analysis();
        cancelled.ChangeStatus(ExecutionStatus.Cancelled, "nova versão", null, "Alex", false, Now);
        Assert.Throws<DomainException>(() => cancelled.AddStep(Def("pedido-y"), null, Now));
    }

    [Fact]
    public void Skill_refining_the_steps_does_not_drop_the_requested_step()
    {
        var plan = Analysis();
        plan.AddStep(Def("pedido-verificar-logs"), null, Now);

        // A skill reenvia as etapas sem a pedida (e com uma pendente nova no fim).
        plan.UpsertSteps([Def("coletar-dados"), Def("causa-raiz"), Def("publicar"), Def("propor-solucoes"), Def("extra")], Now);

        Assert.Equal(["coletar-dados", "causa-raiz", "publicar", "pedido-verificar-logs", "propor-solucoes", "extra"], Keys(plan));
    }

    [Fact]
    public void Pending_step_not_requested_still_leaves_when_omitted()
    {
        var plan = Analysis();
        plan.UpsertSteps([Def("coletar-dados"), Def("causa-raiz"), Def("publicar"), Def("propor-solucoes"), Def("solta")], Now);
        plan.UpsertSteps([Def("coletar-dados"), Def("causa-raiz"), Def("publicar"), Def("propor-solucoes")], Now);
        Assert.DoesNotContain("solta", Keys(plan));
    }
}
