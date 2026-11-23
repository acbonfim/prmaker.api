using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 78720, item #1020: com os PRs mesclados e o PRMake gerado, só sobrou "Validar em QA" (da pessoa) — o fechamento
/// depende dela. O item seguia "Em andamento" girando; agora fica "aguardando" (você).
/// </summary>
public class PersonTurnStatusTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-23T17:00:00Z");

    private static readonly (string Key, string Executor, string[] DependsOn)[] Steps =
    [
        ("corrigir", ExecutionExecutor.Claude, []),
        ("pr", ExecutionExecutor.Claude, ["corrigir"]),
        ("validar-qa", ExecutionExecutor.User, ["pr"]),
        ("gerar-prmake", ExecutionExecutor.Claude, ["pr"]),
        ("fechar-card", ExecutionExecutor.Claude, ["validar-qa", "gerar-prmake"]),
    ];

    private static WorkItem Mirror()
    {
        var inputs = Steps.Select(s => new WorkTaskInput { Title = s.Key, Kind = "research", StepKey = s.Key, Executor = s.Executor }).ToList();
        var item = new WorkItem("78720", "fix", "Correção", null, WorkItemOrigin.Correction, null, Guid.NewGuid(), inputs, null, "Wagner", Now);
        typeof(WorkItem).GetProperty(nameof(WorkItem.Number))!.SetValue(item, 1020L);
        var n = 1020L;
        foreach (var t in item.OrderedTasks)
            typeof(WorkItemTask).GetProperty(nameof(WorkItemTask.Number))!.SetValue(t, ++n);
        foreach (var s in Steps)
            item.SyncStepControl(s.Key, false, null, s.DependsOn, Now);
        return item;
    }

    private static void Done(WorkItem item, params string[] keys)
    {
        foreach (var k in keys)
        {
            item.SyncStep(k, ExecutionStatus.Running, null, "Plano", Now);
            item.SyncStep(k, ExecutionStatus.Completed, null, "Plano", Now);
        }
    }

    [Fact]
    public void Only_the_person_step_left_waits_for_the_person()
    {
        var item = Mirror();
        Done(item, "corrigir", "pr", "gerar-prmake");

        Assert.True(item.OnlyPersonTurn);
        Assert.Equal(WorkItemStatus.Waiting, item.Status);
    }

    [Fact]
    public void Claude_still_has_a_ready_step_so_the_item_stays_running()
    {
        var item = Mirror();
        Done(item, "corrigir", "pr");

        Assert.False(item.OnlyPersonTurn);
        Assert.Equal(WorkItemStatus.Running, item.Status);
    }

    [Fact]
    public void Person_finishes_and_the_item_runs_again()
    {
        var item = Mirror();
        Done(item, "corrigir", "pr", "gerar-prmake");
        item.SyncStep("validar-qa", ExecutionStatus.Completed, null, "Plano", Now);

        Assert.Equal(WorkItemStatus.Running, item.Status);
    }

    [Fact]
    public void Paused_person_step_is_not_the_person_turn()
    {
        var item = Mirror();
        item.SyncStepControl("validar-qa", true, "Alex", ["pr"], Now);
        Done(item, "corrigir", "pr", "gerar-prmake");

        Assert.False(item.OnlyPersonTurn);
        Assert.NotEqual(WorkItemStatus.Waiting, item.Status);
    }
}
