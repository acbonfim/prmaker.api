using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// 0085 (ajustes): o plano de análise também vira item ("Análise inicial do bug/da US #card", espelho das etapas, mesmo
/// nascendo sem etapas) e a sessão ao vivo do item (<c>GET WorkItems/{number}/live</c>).
/// </summary>
public class WorkItemAnalysisLiveTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-05T12:00:00Z");

    private static WorkItem AnalysisItem(IReadOnlyList<WorkTaskInput>? tasks = null, string origin = WorkItemOrigin.Analysis, Guid? planId = null) =>
        new("77434", WorkItemType.Bug, WorkItem.PlanItemTitle(ExecutionPhase.Analysis, "analisar-bug", "77434", "Análise"), null,
            origin, null, planId ?? Guid.NewGuid(), tasks ?? [], Guid.NewGuid(), "Bruno", Now);

    private static WorkTaskInput Step(string key, string title) => new() { Title = title, Kind = WorkTaskKind.Manual, StepKey = key };

    // ---------------------------------------------------------------- análise → item

    [Theory]
    [InlineData("analisar-bug", ExecutionPhase.Analysis, "Análise inicial do bug #77434")]
    [InlineData("analisar-us", ExecutionPhase.Analysis, "Análise inicial da US #77434")]
    [InlineData("ANALISAR-US", ExecutionPhase.Analysis, "Análise inicial da US #77434")]
    [InlineData("analisar-bug", ExecutionPhase.Correction, "Correção 77434")]
    [InlineData("analisar-us", ExecutionPhase.Correction, "Correção 77434")]
    public void Plan_item_title_follows_phase_and_kind(string kind, string phase, string expected) =>
        Assert.Equal(expected, WorkItem.PlanItemTitle(phase, kind, "77434", "Correção 77434"));

    [Fact]
    public void User_story_plan_kind_is_recognized()
    {
        Assert.True(WorkItem.IsUserStoryPlan("analisar-us"));
        Assert.False(WorkItem.IsUserStoryPlan("analisar-bug"));
        Assert.False(WorkItem.IsUserStoryPlan(null));
    }

    [Fact]
    public void Analysis_item_born_without_steps_mirrors_the_plan_and_gains_the_steps()
    {
        var item = AnalysisItem();
        Assert.True(item.MirrorsPlan);
        Assert.Empty(item.Tasks);

        var added = item.AddStepTasks([Step("entender", "Entender o bug"), Step("reproduzir", "Reproduzir")], Now);
        Assert.Equal(2, added.Count);
        Assert.True(item.MirrorsPlan);
        Assert.Equal(["entender", "reproduzir"], item.OrderedTasks.Select(t => t.StepKey).ToArray());

        // Idempotente: a mesma etapa não entra duas vezes; a nova entra no fim.
        var again = item.AddStepTasks([Step("entender", "Entender o bug"), Step("causa", "Causa raiz")], Now);
        Assert.Single(again);
        Assert.Equal(3, item.Tasks.Count);
    }

    [Fact]
    public void Item_of_the_plan_created_by_hand_without_steps_is_not_a_mirror()
    {
        var manual = AnalysisItem([new WorkTaskInput { Title = "Abrir PR", Kind = WorkTaskKind.Manual }], WorkItemOrigin.User);
        Assert.False(manual.MirrorsPlan);
        var noPlan = new WorkItem("77434", WorkItemType.Bug, "Solto", null, WorkItemOrigin.Analysis, null, null, [], null, "Bruno", Now);
        Assert.False(noPlan.MirrorsPlan);
    }

    [Fact]
    public void Analysis_steps_sync_and_cancelled_plan_closes_the_item()
    {
        var item = AnalysisItem([Step("entender", "Entender"), Step("reproduzir", "Reproduzir")]);
        Assert.True(item.SyncStep("entender", ExecutionStatus.Running, null, "Plano", Now));
        Assert.Equal(WorkItemStatus.Running, item.Status);
        item.SyncStep("entender", ExecutionStatus.Completed, null, "Plano", Now);

        // Versão nova da análise: a anterior é cancelada e o item dela fecha.
        Assert.True(item.ClosePlan(ExecutionStatus.Cancelled, "Nova versão da análise", "Plano", Now));
        Assert.Equal(WorkItemStatus.Cancelled, item.Status);
        Assert.Equal(WorkTaskStatus.Completed, item.Tasks.Single(t => t.StepKey == "entender").Status);
        Assert.Equal(WorkTaskStatus.Cancelled, item.Tasks.Single(t => t.StepKey == "reproduzir").Status);
        // Fechado: etapa nova não entra mais.
        Assert.Empty(item.AddStepTasks([Step("causa", "Causa")], Now));
    }

    [Fact]
    public void Analysis_completed_plan_completes_the_item()
    {
        var item = AnalysisItem([Step("entender", "Entender"), Step("reproduzir", "Reproduzir")]);
        item.SyncStep("entender", ExecutionStatus.Completed, null, "Plano", Now);
        Assert.True(item.ClosePlan(ExecutionStatus.Completed, null, "Plano", Now));
        Assert.Equal(WorkItemStatus.Completed, item.Status);
        Assert.Equal(WorkTaskStatus.Skipped, item.Tasks.Single(t => t.StepKey == "reproduzir").Status);
    }

    // ---------------------------------------------------------------- ao vivo

    private static WorkItemLiveHead Head(string status = WorkItemStatus.Running, string? planStatus = null, DateTimeOffset? planLast = null) =>
        new(Guid.NewGuid(), 12, "77434", status, planStatus is null ? null : Guid.NewGuid(), Guid.NewGuid(), null, planStatus, planLast);

    private static WorkItemLiveRequestRow Request(string status, Guid? workerId = null, DateTimeOffset? heartbeat = null,
        string? activity = null, string? recent = null) =>
        new(Guid.NewGuid(), ExecutionRequestKind.WorkItem, status, Guid.NewGuid(), workerId, workerId is null ? null : "mac-bruno",
            workerId is null ? null : Now.AddMinutes(-3), status == ExecutionRequestStatus.Running ? Now.AddMinutes(-3) : null, heartbeat,
            activity, activity is null ? null : "Read", activity is null ? null : Now.AddSeconds(-5), recent);

    private static WorkItemLiveWorker Worker(Guid? id = null, string status = ExecutionWorkerStatus.Active, TimeSpan? seenAgo = null) =>
        new(id ?? Guid.NewGuid(), status, Now - (seenAgo ?? TimeSpan.FromSeconds(20)));

    [Fact]
    public void Running_request_shows_activity_recent_and_no_wait_code()
    {
        var worker = Worker();
        var recent = ExecutionActivity.Serialize([
            new ExecutionActivity { Label = "Lendo WorkItem.cs", Tool = "Read", At = Now.AddSeconds(-5) },
            new ExecutionActivity { Label = "Consultando regras", Tool = "mcp__prmake__prmake_rules", At = Now.AddMinutes(-1) },
        ]);
        var request = Request(ExecutionRequestStatus.Running, worker.Id, Now.AddSeconds(-10), "Lendo WorkItem.cs", recent);

        var live = WorkItemLive.Build(Head(), request, "offline", [], [worker], Now);

        Assert.Equal(12, live.Number);
        Assert.NotNull(live.Request);
        Assert.Equal(ExecutionRequestStatus.Running, live.Request!.Status);
        Assert.Equal("mac-bruno", live.Request.WorkerName);
        Assert.Null(live.Request.WaitCode); // só na fila
        Assert.Equal("Lendo WorkItem.cs", live.Activity!.Label);
        Assert.Equal("Read", live.Activity.Tool);
        Assert.Equal(["Lendo WorkItem.cs", "Consultando regras"], live.Recent.Select(a => a.Label).ToArray());
        Assert.True(live.ExecutorOnline);
    }

    [Fact]
    public void No_request_means_null_request_and_no_activity()
    {
        var live = WorkItemLive.Build(Head(WorkItemStatus.Pending), null, null, [], [], Now);
        Assert.Null(live.Request);
        Assert.Null(live.Activity);
        Assert.Empty(live.Recent);
        Assert.False(live.ExecutorOnline);
    }

    [Fact]
    public void Queued_request_keeps_the_wait_code()
    {
        var live = WorkItemLive.Build(Head(WorkItemStatus.Queued), Request(ExecutionRequestStatus.Queued), "offline", [], [], Now);
        Assert.Equal("offline", live.Request!.WaitCode);
        Assert.False(live.ExecutorOnline);
    }

    [Fact]
    public void Logs_are_the_last_50_with_the_newest_last()
    {
        var id = Guid.NewGuid();
        var logs = Enumerable.Range(0, 60)
            .Select(i => new WorkItemLog(id, null, "Fila", WorkItemLogKind.Info, $"log {i}", Now.AddSeconds(i)))
            .Reverse().ToList();

        var live = WorkItemLive.Build(Head(), null, null, logs, [], Now);

        Assert.Equal(WorkItemLive.MaxLogs, live.Logs.Count);
        Assert.Equal("log 10", live.Logs[0].Text);
        Assert.Equal("log 59", live.Logs[^1].Text);
    }

    [Fact]
    public void Executor_online_follows_the_worker_of_the_running_request()
    {
        var offline = Worker(seenAgo: TimeSpan.FromMinutes(10));
        var running = Request(ExecutionRequestStatus.Running, offline.Id, Now.AddMinutes(-10));
        Assert.False(WorkItemLive.IsExecutorOnline(Head(), running, [offline], Now));

        // O sinal de vida do pedido é recente mesmo sem a máquina na lista.
        var beating = Request(ExecutionRequestStatus.Running, Guid.NewGuid(), Now.AddSeconds(-30));
        Assert.True(WorkItemLive.IsExecutorOnline(Head(), beating, [], Now));
    }

    [Fact]
    public void Without_running_request_needs_an_active_online_worker()
    {
        Assert.True(WorkItemLive.IsExecutorOnline(Head(WorkItemStatus.Pending), null, [Worker()], Now));
        Assert.False(WorkItemLive.IsExecutorOnline(Head(WorkItemStatus.Pending), null, [Worker(status: ExecutionWorkerStatus.Paused)], Now));
        Assert.False(WorkItemLive.IsExecutorOnline(Head(WorkItemStatus.Pending), null, [Worker(status: ExecutionWorkerStatus.Revoked)], Now));
        Assert.False(WorkItemLive.IsExecutorOnline(Head(WorkItemStatus.Pending), null, [Worker(seenAgo: TimeSpan.FromMinutes(3))], Now));
    }

    [Fact]
    public void Plan_signal_in_the_last_five_minutes_counts_as_online()
    {
        Assert.True(WorkItemLive.IsExecutorOnline(Head(planStatus: ExecutionStatus.Running, planLast: Now.AddMinutes(-4)), null, [], Now));
        Assert.False(WorkItemLive.IsExecutorOnline(Head(planStatus: ExecutionStatus.Running, planLast: Now.AddMinutes(-6)), null, [], Now));
        // Plano terminado não conta.
        Assert.False(WorkItemLive.IsExecutorOnline(Head(planStatus: ExecutionStatus.Completed, planLast: Now.AddMinutes(-1)), null, [], Now));
    }

    [Fact]
    public void Owner_of_the_workers_is_the_request_then_plan_then_creator_then_viewer()
    {
        var viewer = Guid.NewGuid();
        var creator = Guid.NewGuid();
        var planOwner = Guid.NewGuid();
        var head = new WorkItemLiveHead(Guid.NewGuid(), 1, "1", WorkItemStatus.Pending, Guid.NewGuid(), creator, planOwner, ExecutionStatus.Running, null);
        var request = Request(ExecutionRequestStatus.Queued);
        Assert.Equal(request.OwnerUserId, WorkItemLive.OwnerOf(head, request, viewer));
        Assert.Equal(planOwner, WorkItemLive.OwnerOf(head, null, viewer));
        Assert.Equal(creator, WorkItemLive.OwnerOf(head with { PlanOwnerUserId = null }, null, viewer));
        Assert.Equal(viewer, WorkItemLive.OwnerOf(head with { PlanOwnerUserId = null, CreatedBy = null }, null, viewer));
    }
}
