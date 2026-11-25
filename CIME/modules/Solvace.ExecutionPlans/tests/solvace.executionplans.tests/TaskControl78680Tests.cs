using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 78680, item #950: o plano pedia à pessoa o HAR (#951) sem uma sub-tarefa do Claude para registrar o pedido no card.
/// Para pôr essa sub-tarefa na frente, o especialista cancelava o item inteiro e recriava. Agora: acrescentar no início,
/// mover (início/fim/acima/abaixo/antes de outra), pausar/continuar e cancelar só uma — sem quebrar dependência.
/// </summary>
public class TaskControl78680Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-19T18:00:00Z");

    private static ExecutionStepDefinition Def(string key, string executor = ExecutionExecutor.Claude, params string[] dependsOn) =>
        new() { Key = key, Title = key, Executor = executor, DependsOn = dependsOn.ToList() };

    /// <summary>Como o plano do #950: coletar-har (pessoa) → analisar-har → decidir-correcao → fechar-card, em cadeia.</summary>
    private static ExecutionPlan Plan950()
    {
        var plan = new ExecutionPlan("78680", "analisar-bug", "Correção 78680", null, Guid.NewGuid(), "Alex", Now,
            ExecutionPhase.Correction, Guid.NewGuid());
        plan.UpsertSteps([
            Def("coletar-har", ExecutionExecutor.User),
            Def("analisar-har", ExecutionExecutor.Claude, "coletar-har"),
            Def("decidir-correcao", ExecutionExecutor.Claude, "analisar-har"),
            Def("fechar-card", ExecutionExecutor.Claude, "decidir-correcao"),
        ], Now);
        return plan;
    }

    private static string[] Keys(ExecutionPlan plan) => plan.Steps.OrderBy(s => s.Order).Select(s => s.Key).ToArray();

    private static ExecutionStep Step(ExecutionPlan plan, string key) => plan.Steps.Single(s => s.Key == key);

    [Fact]
    public void Requested_step_goes_first_and_the_skill_does_not_move_it_back()
    {
        var plan = Plan950();

        plan.AddStep(Def("pedido-registrar-pedido-do-har"), null, Now, TaskPosition.First);
        Assert.Equal(["pedido-registrar-pedido-do-har", "coletar-har", "analisar-har", "decidir-correcao", "fechar-card"], Keys(plan));

        // A skill refina as etapas mandando a ordem antiga: a pedida continua na frente.
        plan.UpsertSteps([
            Def("coletar-har", ExecutionExecutor.User),
            Def("analisar-har", ExecutionExecutor.Claude, "coletar-har"),
            Def("decidir-correcao", ExecutionExecutor.Claude, "analisar-har"),
            Def("fechar-card", ExecutionExecutor.Claude, "decidir-correcao"),
        ], Now);
        Assert.Equal("pedido-registrar-pedido-do-har", Keys(plan)[0]);
        Assert.Equal("pedido-registrar-pedido-do-har", plan.ReadySteps().First().Key);
    }

    [Fact]
    public void Moved_step_keeps_its_place_when_the_skill_refines_the_steps()
    {
        var plan = Plan950();
        plan.UpsertSteps([
            Def("coletar-har", ExecutionExecutor.User),
            Def("analisar-har", ExecutionExecutor.Claude, "coletar-har"),
            Def("decidir-correcao", ExecutionExecutor.Claude, "analisar-har"),
            Def("fechar-card"),
            Def("avisar-cliente"),
        ], Now);

        plan.MoveStep("avisar-cliente", TaskPosition.First, null, "Alex", Now);
        Assert.Equal("avisar-cliente", Keys(plan)[0]);

        plan.UpsertSteps([Def("coletar-har", ExecutionExecutor.User), Def("analisar-har", ExecutionExecutor.Claude, "coletar-har"),
            Def("decidir-correcao", ExecutionExecutor.Claude, "analisar-har"), Def("fechar-card"), Def("avisar-cliente")], Now);
        Assert.Equal("avisar-cliente", Keys(plan)[0]);
    }

    [Fact]
    public void Step_cannot_jump_ahead_of_what_it_depends_on()
    {
        var plan = Plan950();

        var e = Assert.Throws<DomainException>(() => plan.MoveStep("analisar-har", TaskPosition.First, null, "Alex", Now));
        Assert.Contains("depende de 'coletar-har'", e.Message);
        e = Assert.Throws<DomainException>(() => plan.MoveStep("coletar-har", TaskPosition.Last, null, "Alex", Now));
        Assert.Contains("depende de 'coletar-har'", e.Message);
        Assert.Equal(["coletar-har", "analisar-har", "decidir-correcao", "fechar-card"], Keys(plan));
    }

    [Fact]
    public void Moving_ahead_of_the_running_step_interrupts_it()
    {
        var plan = new ExecutionPlan("78680", "analisar-bug", "x", null, Guid.NewGuid(), "Alex", Now, ExecutionPhase.Correction, Guid.NewGuid());
        plan.UpsertSteps([Def("a"), Def("b"), Def("c")], Now);
        plan.UpdateStep("a", ExecutionStatus.Running, null, null, null, null, null, "claude", Now);

        var interrupted = plan.MoveStep("c", TaskPosition.First, null, "Alex", Now);

        Assert.Equal(["c", "a", "b"], Keys(plan));
        Assert.Equal("a", Assert.Single(interrupted).Key);
        Assert.Equal(ExecutionStatus.Pending, Step(plan, "a").Status);
        Assert.Equal("c", plan.ReadySteps().First().Key);
    }

    [Fact]
    public void Held_step_and_its_dependents_are_not_ready_and_cannot_start()
    {
        var plan = Plan950();
        plan.CompleteStep("coletar-har", "HAR anexado", "Alex", Now, false);

        plan.HoldStep("analisar-har", true, "Alex", Now);

        Assert.Empty(plan.ReadySteps());
        Assert.Contains("pausada", Assert.Throws<DomainException>(() =>
            plan.UpdateStep("analisar-har", ExecutionStatus.Running, null, null, null, null, null, "claude", Now)).Message);

        plan.HoldStep("analisar-har", false, "Alex", Now);
        Assert.Equal("analisar-har", plan.ReadySteps().Single().Key);
    }

    [Fact]
    public void Dependents_of_a_step_are_listed_transitively()
    {
        var plan = Plan950();
        Assert.Equal(["analisar-har", "decidir-correcao", "fechar-card"], plan.DependentsOf("coletar-har").Select(s => s.Key));
    }

    // ---------------------------------------------------------------- item avulso

    private static WorkItem Loose(params WorkTaskInput[] tasks)
    {
        var item = new WorkItem("78680", "task", "Promoção", null, WorkItemOrigin.User, null, null, tasks.ToList(), null, "Alex", Now);
        typeof(WorkItem).GetProperty(nameof(WorkItem.Number))!.SetValue(item, 1000L);
        var n = 1000L;
        foreach (var t in item.OrderedTasks)
            typeof(WorkItemTask).GetProperty(nameof(WorkItemTask.Number))!.SetValue(t, ++n);
        return item;
    }

    private static WorkTaskInput T(string title, string kind, string? repo = null) =>
        new() { Title = title, Kind = kind, Params = repo is null ? null : new Dictionary<string, string> { ["repository"] = repo } };

    private static long[] Numbers(WorkItem item) => item.OrderedTasks.Select(t => t.Number).ToArray();

    [Fact]
    public void Pr_depends_on_the_branch_by_kind_and_cannot_go_first()
    {
        var item = Loose(T("Branch", "branch", "api"), T("Cherry-pick", "cherry-pick", "api"), T("PR", "pr", "api"), T("Avisar", "notify"));

        var c = item.Controls(item.Tasks.Single(t => t.Number == 1003));
        Assert.Equal([1001L, 1002], c.DependsOn);
        Assert.True(c.Implicit);
        Assert.Contains("depende da #1001", c.First);
        Assert.Null(item.Controls(item.Tasks.Single(t => t.Number == 1004)).First);
        Assert.Equal([1002L, 1003], item.Controls(item.Tasks.Single(t => t.Number == 1001)).Dependents);

        Assert.Throws<DomainException>(() => item.MoveTaskTo(1003, TaskPosition.First, null, "Alex", Now));
        item.MoveTaskTo(1004, TaskPosition.First, null, "Alex", Now);
        Assert.Equal([1004L, 1001, 1002, 1003], Numbers(item));
    }

    [Fact]
    public void Task_moves_up_down_first_and_last()
    {
        var item = Loose(T("A", "task"), T("B", "task"), T("C", "task"));
        item.MoveTaskTo(1003, TaskPosition.Up, null, "Alex", Now);
        Assert.Equal([1001L, 1003, 1002], Numbers(item));
        item.MoveTaskTo(1001, TaskPosition.Down, null, "Alex", Now);
        Assert.Equal([1003L, 1001, 1002], Numbers(item));
        item.MoveTaskTo(1003, TaskPosition.Last, null, "Alex", Now);
        Assert.Equal([1001L, 1002, 1003], Numbers(item));
        Assert.Equal("Já é a primeira ainda não feita.", item.Controls(item.Tasks.Single(t => t.Number == 1001)).First);
    }

    [Fact]
    public void Running_task_with_the_executor_needs_an_interrupt()
    {
        var item = Loose(T("A", "task"), T("B", "task"));
        item.MarkQueued(Guid.NewGuid(), "Alex", Now);
        item.ApplyTaskAction(1001, WorkTaskAction.Start, null, null, null, "Claude", Now);

        Assert.Throws<WorkTaskInterruptRequiredException>(() => item.MoveTaskTo(1002, TaskPosition.First, null, "Alex", Now));

        // Executor parado (pedido cancelado): a em andamento volta a pendente e a movida passa na frente.
        item.AttachRequest(item.ActiveRequestId!.Value, ExecutionRequestStatus.Cancelled, Now);
        item.MoveTaskTo(1002, TaskPosition.First, null, "Alex", Now);
        Assert.Equal([1002L, 1001], Numbers(item));
        Assert.Equal(WorkTaskStatus.Pending, item.Tasks.Single(t => t.Number == 1001).Status);
        Assert.Equal(WorkItemStatus.Pending, item.Status);
    }

    [Fact]
    public void Held_task_is_skipped_with_its_dependents_and_cannot_start()
    {
        var item = Loose(T("Branch", "branch", "api"), T("Código", "code", "api"), T("Avisar", "notify"));

        item.HoldTask(1001, true, "esperar o cliente", "Alex", Now);

        Assert.Equal(1003, item.NextTask!.Number);
        Assert.Equal(1001, item.Controls(item.Tasks.Single(t => t.Number == 1002)).WaitsForHeld);
        Assert.Contains("pausada", Assert.Throws<DomainException>(() =>
            item.ApplyTaskAction(1001, WorkTaskAction.Start, null, null, null, "Claude", Now)).Message);

        item.ApplyTaskAction(1003, WorkTaskAction.Complete, "ok", null, null, "Claude", Now);
        Assert.True(item.OnlyHeldLeft);
        Assert.Equal(WorkItemStatus.Pending, item.Status);

        item.HoldTask(1001, false, null, "Alex", Now);
        Assert.Equal(1001, item.NextTask!.Number);
    }

    [Fact]
    public void Executor_finishing_with_only_held_tasks_left_leaves_the_item_pending()
    {
        var item = Loose(T("A", "task"), T("B", "task"));
        var request = Guid.NewGuid();
        item.MarkQueued(request, "Alex", Now);
        item.HoldTask(1002, true, null, "Alex", Now);
        item.ApplyTaskAction(1001, WorkTaskAction.Complete, "ok", null, null, "Claude", Now);

        item.AttachRequest(request, ExecutionRequestStatus.Done, Now);

        Assert.Equal(WorkItemStatus.Pending, item.Status);
    }

    [Fact]
    public void Cancel_one_task_with_or_without_its_dependents()
    {
        var item = Loose(T("Script", "sql-script"), T("Chamado", "ticket"), T("Avisar", "notify"));
        Assert.Equal([1002L], item.Controls(item.Tasks.Single(t => t.Number == 1001)).Dependents);

        var cancelled = item.CancelTask(1001, "não precisa", cascade: true, "Alex", Now);

        Assert.Equal([1001L, 1002], cancelled.Select(t => t.Number));
        Assert.Equal(WorkTaskStatus.Pending, item.Tasks.Single(t => t.Number == 1003).Status);
        Assert.NotEqual(WorkItemStatus.Cancelled, item.Status);

        var other = Loose(T("Script", "sql-script"), T("Chamado", "ticket"));
        other.CancelTask(1001, null, cascade: false, "Alex", Now);
        Assert.Equal(WorkTaskStatus.Pending, other.Tasks.Single(t => t.Number == 1002).Status);
    }

    [Fact]
    public void Added_task_goes_first_in_a_loose_item()
    {
        var item = Loose(T("A", "task"), T("B", "task"));
        item.ApplyTaskAction(1001, WorkTaskAction.Complete, "ok", null, null, "Claude", Now);

        item.AddTasks([new WorkTaskInput { Title = "Novo", Kind = "comment" }], null, "Alex", Now, first: true);

        Assert.Equal(["A", "Novo", "B"], item.OrderedTasks.Select(t => t.Title));
    }

    [Fact]
    public void Mirror_item_follows_the_step_order_hold_and_dependencies()
    {
        var plan = Plan950();
        var item = new WorkItem("78680", WorkItemType.Fix, "Correção", null, WorkItemOrigin.Correction, null, plan.Id,
            plan.Steps.OrderBy(s => s.Order).Select(s => new WorkTaskInput { Title = s.Title, Kind = WorkTaskKind.Manual, StepKey = s.Key }).ToList(),
            Guid.NewGuid(), "Alex", Now);
        var n = 950L;
        foreach (var t in item.OrderedTasks)
            typeof(WorkItemTask).GetProperty(nameof(WorkItemTask.Number))!.SetValue(t, ++n);

        plan.AddStep(Def("pedido-pedir-har"), null, Now, TaskPosition.First);
        plan.HoldStep("fechar-card", true, "Alex", Now);
        item.AddStepTasks(plan.Steps.Select(s => new WorkTaskInput { Title = s.Title, Kind = WorkTaskKind.Manual, StepKey = s.Key }), Now);
        Assert.True(item.ReorderByStep(plan.Steps.ToDictionary(s => s.Key, s => s.Order)));
        foreach (var s in plan.Steps)
            item.SyncStepControl(s.Key, s.Held, s.HeldBy, s.DependsOn, Now);

        Assert.Equal("pedido-pedir-har", item.OrderedTasks.First().StepKey);
        var analisar = item.Tasks.Single(t => t.StepKey == "analisar-har");
        Assert.Equal([951L], analisar.DependsOn);
        Assert.Contains("depende da #951", item.Controls(analisar).First);
        Assert.True(item.Tasks.Single(t => t.StepKey == "fechar-card").Held);
    }
}
