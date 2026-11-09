using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>"Já resolvi": sub-tarefa do Claude que parou esperando a pessoa (ex.: sem acesso ao banco — card 77988, #81).</summary>
public class WorkTaskResolveTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-08T22:00:00Z");

    private static WorkItem Item(string kind = "sql-query", string executor = "claude") =>
        new("77988", "data", "Validar a migration", null, WorkItemOrigin.Specialist, null, null,
            [new WorkTaskInput { Kind = kind, Title = "Validar no banco de DEV", Executor = executor }], null, "Alex", Now);

    [Fact]
    public void Waiting_claude_task_goes_back_to_pending_with_the_note()
    {
        var item = Item();
        var task = item.OrderedTasks.Single();
        item.ApplyTaskAction(task.Number, WorkTaskAction.Start, null, null, null, "Claude", Now);
        item.ApplyTaskAction(task.Number, WorkTaskAction.Wait, "**Sem acesso ao banco GLOBAL de DEV.**", null, null, "Claude", Now);
        Assert.Equal(WorkItemStatus.Waiting, item.Status);

        item.ResolveWaitingTask(task.Number, "  adicionei o alias dev-global  ", "Alex", Now.AddMinutes(5));

        Assert.Equal(WorkTaskStatus.Pending, task.Status);
        Assert.Equal("**Sem acesso ao banco GLOBAL de DEV.**\n\n**Resolvido por Alex:** adicionei o alias dev-global", task.Result);
        Assert.Equal(WorkItemStatus.Pending, item.Status);
        Assert.Contains(item.Logs, l => l.Text.Contains("resolvido — volta para o Claude — adicionei o alias dev-global"));
    }

    [Fact]
    public void Without_note_only_says_who_resolved()
    {
        var item = Item();
        var task = item.OrderedTasks.Single();
        item.ApplyTaskAction(task.Number, WorkTaskAction.Wait, "Falta credencial.", null, null, "Claude", Now);

        item.ResolveWaitingTask(task.Number, null, "Alex", Now);

        Assert.EndsWith("**Resolvido por Alex.**", task.Result);
    }

    [Fact]
    public void Only_waiting_tasks_of_claude_without_open_ticket()
    {
        var item = Item();
        var task = item.OrderedTasks.Single();
        Assert.Throws<DomainException>(() => item.ResolveWaitingTask(task.Number, null, "Alex", Now)); // pendente

        var own = Item("manual", "user");
        var t = own.OrderedTasks.Single();
        own.ApplyTaskAction(t.Number, WorkTaskAction.Wait, "Faça X.", null, null, "Claude", Now);
        Assert.Contains("use Concluir", Assert.Throws<DomainException>(() => own.ResolveWaitingTask(t.Number, null, "Alex", Now)).Message);

        var ticket = Item("ticket");
        var tt = ticket.OrderedTasks.Single();
        var (action, result) = WorkTaskTicket.TaskAction(ExecutionLinkStatus.Open, "#9");
        ticket.ApplyTaskAction(tt.Number, action, result, WorkTaskTicket.Output("https://x/tickets/9", "#9", ExecutionLinkStatus.Open), null, "Alex", Now);
        Assert.Contains("chamado", Assert.Throws<DomainException>(() => ticket.ResolveWaitingTask(tt.Number, null, "Alex", Now)).Message);
    }

    [Fact]
    public void Closed_item_cannot_be_resumed()
    {
        var item = Item();
        var task = item.OrderedTasks.Single();
        item.ApplyTaskAction(task.Number, WorkTaskAction.Wait, "Sem acesso.", null, null, "Claude", Now);
        item.Cancel("não precisa mais", "Alex", Now);

        Assert.Throws<DomainException>(() => item.ResolveWaitingTask(task.Number, null, "Alex", Now));
    }
}
