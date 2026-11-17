using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 78505, item #854: as sub-tarefas espelho da análise terminavam sem resultado (o resumo do advance ficava só no
/// log da etapa). O resultado da etapa vai para a sub-tarefa assim que ela conclui — e pode ser completado depois.
/// </summary>
public class StepResultTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T13:00:00Z");

    private static WorkItem Mirror()
    {
        var inputs = new[] { "consultar-base", "consultar-ambiente" }
            .Select(k => new WorkTaskInput { Title = k, Kind = "research", StepKey = k }).ToList();
        var item = new WorkItem("78505", "analysis", "Análise", null, WorkItemOrigin.Analysis, null, Guid.NewGuid(), inputs, null, "Alex", Now);
        typeof(WorkItem).GetProperty(nameof(WorkItem.Number))!.SetValue(item, 854L);
        var n = 854L;
        foreach (var t in item.OrderedTasks)
            typeof(WorkItemTask).GetProperty(nameof(WorkItemTask.Number))!.SetValue(t, ++n);
        return item;
    }

    [Fact]
    public void Completed_step_result_lands_on_the_mirror_task_and_can_be_replaced()
    {
        var item = Mirror();
        item.SyncStep("consultar-ambiente", ExecutionStatus.Completed, null, "Plano", Now);
        var task = item.Tasks.Single(t => t.StepKey == "consultar-ambiente");
        Assert.Null(task.Result);

        item.AnnotateTask(task.Number, "Resumo curto", null, null, Now);
        item.AnnotateTask(task.Number, "| Campo | Valor |\n|---|---|\n| IDLE_TIMEOUT | 240 |", null, null, Now.AddMinutes(1));

        Assert.Equal(WorkTaskStatus.Completed, task.Status);
        Assert.StartsWith("| Campo | Valor |", task.Result);
    }

    [Fact]
    public void Result_only_request_is_detected()
    {
        Assert.True(new UpdateExecutionStepRequest { Result = "**Main**" }.OnlyResult);
        Assert.False(new UpdateExecutionStepRequest { Result = "x", Status = "completed" }.OnlyResult);
        Assert.False(new UpdateExecutionStepRequest { Status = "completed" }.OnlyResult);
        Assert.False(new UpdateExecutionStepRequest { Result = "  " }.OnlyResult);
    }
}
