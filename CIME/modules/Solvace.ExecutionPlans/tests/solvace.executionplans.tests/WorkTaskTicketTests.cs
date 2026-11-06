using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>Chamado na Bancada: chamado anexado à sub-tarefa e etapa de PR com PR aberto aguardando o merge.</summary>
public class WorkTaskTicketTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-06T12:00:00Z");

    private static WorkItem Item() =>
        new("77988", "ticket", "Chamado de limpeza", null, WorkItemOrigin.User, null, null,
            [new WorkTaskInput { Kind = "ticket", Title = "Abrir o chamado", Executor = "user" }], null, "Alex", Now);

    private static void Ticket(WorkItem item, long task, string url, string? title, string status)
    {
        var (action, result) = WorkTaskTicket.TaskAction(status, WorkTaskTicket.DisplayName(url, title));
        item.ApplyTaskAction(task, action, result, WorkTaskTicket.Output(url, title, status), null, "Alex", Now);
    }

    [Fact]
    public void Attached_ticket_waits_and_shows_on_the_task()
    {
        var item = Item();
        var task = item.OrderedTasks.Single();

        Ticket(item, task.Number, "https://solvace.freshservice.com/helpdesk/tickets/123", "#123 Limpar fotos", ExecutionLinkStatus.Open);

        Assert.Equal(WorkTaskStatus.Waiting, task.Status);
        Assert.Equal("Aguardando o chamado #123 Limpar fotos", task.Result);
        var link = Assert.Single(WorkTaskResponse.From(task).Links);
        Assert.Equal(("ticket", "https://solvace.freshservice.com/helpdesk/tickets/123", "#123 Limpar fotos", "open"),
            (link.Kind, link.Url, link.Title, link.Status));
    }

    [Fact]
    public void Resolved_ticket_completes_the_task_and_closed_goes_back_to_the_person()
    {
        var item = Item();
        var task = item.OrderedTasks.Single();
        Ticket(item, task.Number, "https://x/tickets/9", null, ExecutionLinkStatus.Open);

        Ticket(item, task.Number, "https://x/tickets/9", null, ExecutionLinkStatus.Closed);
        Assert.Equal(WorkTaskStatus.Waiting, task.Status);
        Assert.StartsWith("Chamado https://x/tickets/9 fechado sem resolução", task.Result);

        Ticket(item, task.Number, "https://x/tickets/9", null, ExecutionLinkStatus.Resolved);
        Assert.Equal(WorkTaskStatus.Completed, task.Status);
        Assert.Equal("resolved", WorkTaskResponse.From(task).Links.Single().Status);
    }

    [Fact]
    public void Attached_ticket_is_a_deliverable_of_the_task()
    {
        var item = Item();
        var task = item.OrderedTasks.Single();
        Ticket(item, task.Number, "https://x/tickets/9", "#9 Limpar fotos", ExecutionLinkStatus.Resolved);

        var d = Assert.Single(WorkDeliverableListing.FromOutput(task));
        Assert.Equal((WorkDeliverableKind.Ticket, "https://x/tickets/9", "#9 Limpar fotos", "resolved"), (d.Kind, d.Url, d.Title, d.Status));
    }

    [Fact]
    public void Ticket_input_is_validated()
    {
        Assert.Throws<DomainException>(() => WorkTaskTicket.NormalizeUrl("  "));
        Assert.Throws<DomainException>(() => WorkTaskTicket.NormalizeStatus("merged"));
        Assert.Equal("resolved", WorkTaskTicket.NormalizeStatus(" Resolved "));
        Assert.Null(WorkTaskTicket.NormalizeTitle(" "));
        Assert.Empty(WorkTaskResponse.From(Item().OrderedTasks.Single()).Links);
    }

    private static ExecutionPlan Plan()
    {
        var plan = new ExecutionPlan("77988", "analisar-us", "Implementação 77988", null, Guid.NewGuid(), "Alex", Now, ExecutionPhase.Correction, Guid.NewGuid());
        plan.UpsertSteps(
        [
            new ExecutionStepDefinition { Key = "validar", Title = "Validar", Kind = ExecutionStepKind.Validation, Executor = ExecutionExecutor.Claude },
            new ExecutionStepDefinition { Key = "pr-edv-solvace", Title = "PRs", Kind = ExecutionStepKind.PullRequest, Executor = ExecutionExecutor.Claude, Repository = "edv-solvace", DependsOn = ["validar"] },
        ], Now);
        return plan;
    }

    private static ExecutionLink Pr(ExecutionPlan plan, int number, string target, string status = ExecutionLinkStatus.Open)
    {
        var link = new ExecutionLink(plan.Id, "pr-edv-solvace", $"https://github.com/electradv/edv-solvace/pull/{number}", $"#{number} edv-solvace → {target}",
            ExecutionLinkKind.PullRequest, false, number, "edv-solvace", target, "PRMake", Now);
        if (status != ExecutionLinkStatus.Open) link.ChangeStatus(status, "GitHub", Now);
        return link;
    }

    [Fact]
    public void Pending_pr_step_with_open_prs_waits_for_the_merge()
    {
        var plan = Plan();
        var step = plan.WaitForPullRequests("pr-edv-solvace", [Pr(plan, 31252, "development"), Pr(plan, 31253, "qa")], "PRMake", Now, false);

        Assert.NotNull(step);
        Assert.Equal(ExecutionStatus.Waiting, step!.Status);
        Assert.Equal(ExecutionWaitingOn.External, step.WaitingOn);
        Assert.Equal("Aguardando o merge de 2 PRs", step.StatusReason);
        Assert.DoesNotContain(plan.ReadySteps(), s => s.Key == "pr-edv-solvace");
    }

    [Fact]
    public void Pr_step_without_open_pr_or_already_running_is_left_alone()
    {
        var plan = Plan();
        Assert.Null(plan.WaitForPullRequests("pr-edv-solvace", [Pr(plan, 1, "development", ExecutionLinkStatus.Merged)], "PRMake", Now, false));
        Assert.Null(plan.WaitForPullRequests("pr-edv-solvace", [], "PRMake", Now, false));

        plan.CompleteStep("validar", "ok", "Claude", Now, true);
        plan.StartStepByUser("pr-edv-solvace", "Claude", Now);
        Assert.Null(plan.WaitForPullRequests("pr-edv-solvace", [Pr(plan, 2, "development")], "PRMake", Now, false));
        Assert.Null(plan.WaitForPullRequests("validar", [Pr(plan, 2, "development")], "PRMake", Now, false, includeRunning: true));
    }

    [Fact]
    public void Pr_attached_to_the_running_step_waits_for_the_merge()
    {
        var plan = Plan();
        plan.CompleteStep("validar", "ok", "Claude", Now, true);
        plan.StartStepByUser("pr-edv-solvace", "Claude", Now);

        var step = plan.WaitForPullRequests("pr-edv-solvace", [Pr(plan, 31252, "development")], "Claude", Now, true, includeRunning: true);

        Assert.Equal(ExecutionStatus.Waiting, step!.Status);
        Assert.Equal("Aguardando o merge do PR #31252 edv-solvace → development", step.StatusReason);
    }
}
