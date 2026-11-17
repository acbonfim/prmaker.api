using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 78505, item #877: o Especialista (guardião do caso) não conseguia pôr a sub-tarefa nova (#882) antes da que estava em
/// espera (#881), nem editar a em espera (#883), nem ajustar os critérios do item já começado — pulava e recriava
/// sub-tarefas, e o item ficava "em andamento" sem nada na fila.
/// </summary>
public class WorkItemGuardianTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T14:00:00Z");

    /// <summary>Como o #877: 878–880 feitas, 881 em espera (sem ação para gravar na US).</summary>
    private static WorkItem Item877()
    {
        var inputs = new[] { "Levantar", "Analisar", "Propor", "Registrar na US" }
            .Select(t => new WorkTaskInput { Title = t, Kind = "research" }).ToList();
        var item = new WorkItem("78505", "analysis", "Manter conectado", "critérios", WorkItemOrigin.Specialist, null, null, inputs, null, "Alex", Now);
        typeof(WorkItem).GetProperty(nameof(WorkItem.Number))!.SetValue(item, 877L);
        Renumber(item);
        foreach (var n in new long[] { 878, 879, 880 })
            item.ApplyTaskAction(n, WorkTaskAction.Complete, "ok", null, null, "Claude", Now);
        item.ApplyTaskAction(881, WorkTaskAction.Start, null, null, null, "Claude", Now);
        item.ApplyTaskAction(881, WorkTaskAction.Wait, "sem ação para gravar os campos", null, null, "Claude", Now);
        Assert.Equal(WorkItemStatus.Waiting, item.Status);
        return item;
    }

    private static void Renumber(WorkItem item)
    {
        var n = Math.Max(item.Number, item.Tasks.Max(t => t.Number));
        foreach (var t in item.OrderedTasks.Where(t => t.Number == 0))
            typeof(WorkItemTask).GetProperty(nameof(WorkItemTask.Number))!.SetValue(t, ++n);
    }

    private static WorkItemTask Task(WorkItem item, long n) => item.Tasks.Single(t => t.Number == n);

    [Fact]
    public void New_task_goes_before_the_waiting_one_which_runs_again_after_it()
    {
        var item = Item877();

        item.AddTasks([new WorkTaskInput { Title = "Protótipo", Kind = "task" }], 881, "Especialista", Now);
        Renumber(item);

        Assert.Equal([878L, 879, 880, 882, 881], item.OrderedTasks.Select(t => t.Number));
        Assert.Equal(WorkTaskStatus.Pending, Task(item, 881).Status);
        Assert.Contains("refazer", Task(item, 881).Result);
        Assert.Equal(WorkItemStatus.Pending, item.Status); // parado, com Executar — não "em andamento" sem fila
    }

    [Fact]
    public void Task_moves_before_the_waiting_one()
    {
        var item = Item877();
        item.AddTasks([new WorkTaskInput { Title = "Protótipo", Kind = "task" }], null, "Especialista", Now);
        Renumber(item);
        Assert.Equal([878L, 879, 880, 881, 882], item.OrderedTasks.Select(t => t.Number));

        item.MoveTask(882, 881, "Especialista", Now);

        Assert.Equal([878L, 879, 880, 882, 881], item.OrderedTasks.Select(t => t.Number));
        Assert.Equal([1, 2, 3, 4, 5], item.OrderedTasks.Select(t => t.Order));
        Assert.Equal(WorkTaskStatus.Pending, Task(item, 881).Status);
        Assert.Equal(WorkItemStatus.Pending, item.Status);
        Assert.Contains("já saiu da fila", Assert.Throws<DomainException>(() => item.MoveTask(882, 878, "Especialista", Now)).Message);
        Assert.Contains("já saiu da fila", Assert.Throws<DomainException>(() => item.MoveTask(878, null, "Especialista", Now)).Message);

        item.MoveTask(882, null, "Especialista", Now);
        Assert.Equal([878L, 879, 880, 881, 882], item.OrderedTasks.Select(t => t.Number));
    }

    [Fact]
    public void Waiting_task_gets_a_new_instruction_and_goes_back_to_pending()
    {
        var item = Item877();

        item.EditTask(881, new WorkTaskInput { Description = "Incluir os termos cadastrados dos textos novos." }, "Especialista", Now);

        Assert.Equal(WorkTaskStatus.Pending, Task(item, 881).Status);
        Assert.Equal("Incluir os termos cadastrados dos textos novos.", Task(item, 881).Description);
        Assert.Equal(WorkItemStatus.Pending, item.Status);
    }

    [Fact]
    public void Started_item_gets_new_acceptance_criteria_but_not_a_new_task_list()
    {
        var item = Item877();

        item.Update("Manter conectado", "critérios + termos cadastrados", null, "Especialista", Now);
        Assert.Equal("critérios + termos cadastrados", item.Description);

        Assert.Throws<WorkItemNotEditableException>(() =>
            item.Update("Manter conectado", null, [new WorkTaskInput { Title = "X" }], "Especialista", Now));
    }

    [Fact]
    public void Skipping_the_waiting_task_leaves_the_item_pending_not_running_without_a_request()
    {
        var item = Item877();
        item.AddTasks([new WorkTaskInput { Title = "Protótipo", Kind = "task" }], null, "Especialista", Now);

        item.ApplyTaskAction(881, WorkTaskAction.Skip, "substituída", null, null, "Especialista", Now);

        Assert.Equal(WorkItemStatus.Pending, item.Status);
        item.MarkQueued(Guid.NewGuid(), "Alex", Now);
        Assert.Equal(WorkItemStatus.Queued, item.Status);
    }

    [Fact]
    public void Executor_between_tasks_keeps_the_item_running()
    {
        var item = Item877();
        item.EditTask(881, new WorkTaskInput { Description = "de novo" }, "Especialista", Now);
        item.AddTasks([new WorkTaskInput { Title = "Protótipo", Kind = "task" }], null, "Especialista", Now);
        Renumber(item);
        var request = Guid.NewGuid();
        item.MarkQueued(request, "Alex", Now);
        item.AttachRequest(request, ExecutionRequestStatus.Running, Now);

        item.ApplyTaskAction(881, WorkTaskAction.Start, null, null, null, "Claude", Now);
        item.ApplyTaskAction(881, WorkTaskAction.Complete, "ok", null, null, "Claude", Now);

        Assert.Equal(WorkItemStatus.Running, item.Status);
    }
}
