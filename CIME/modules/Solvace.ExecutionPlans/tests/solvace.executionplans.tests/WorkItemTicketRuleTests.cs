using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0085 r5 (1.1/1.4): item com script já nasce com as sub-tarefas do chamado.</summary>
public class WorkItemTicketRuleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-05T12:00:00Z");

    private static WorkTaskInput T(string kind, string title = "x", string executor = "claude") =>
        new() { Kind = kind, Title = title, Executor = executor };

    [Fact]
    public void Script_gets_the_ticket_pair_right_after_the_last_script()
    {
        var tasks = new List<WorkTaskInput>
        {
            T("sql-query", "Consulta antes"), T("sql-script", "Script 1"), T("sql-script", "Script 2"), T("validation", "Conferir depois")
        };

        var result = WorkItemTicketRule.Apply(tasks, "Freshservice, Jira");

        Assert.Equal(["sql-query", "sql-script", "sql-script", "ticket", "ticket", "validation"], result.Select(t => t.Kind));
        var generate = result[3];
        var open = result[4];
        Assert.Equal(WorkItemTicketRule.GenerateTitle, generate.Title);
        Assert.Equal("claude", generate.Executor);
        Assert.Equal(WorkItemTicketRule.OpenTitle, open.Title);
        Assert.Equal("user", open.Executor);
        Assert.Equal("Freshservice", open.Params!["system"]);
        Assert.Equal("Freshservice", generate.Params!["system"]);
    }

    [Fact]
    public void Item_that_already_has_a_ticket_is_left_alone()
    {
        var tasks = new List<WorkTaskInput> { T("sql-script"), T("ticket", "Abrir chamado", "user") };
        Assert.Same(tasks, WorkItemTicketRule.Apply(tasks, "Freshservice"));
    }

    [Fact]
    public void Item_without_script_is_left_alone()
    {
        var tasks = new List<WorkTaskInput> { T("branch"), T("pr") };
        Assert.Same(tasks, WorkItemTicketRule.Apply(tasks, "Freshservice"));
        Assert.Empty(WorkItemTicketRule.Apply([], "Freshservice"));
    }

    [Fact]
    public void Without_ticket_system_the_param_is_omitted_and_the_item_is_valid()
    {
        var result = WorkItemTicketRule.Apply([T("SQL-Script", "Script")], null);
        Assert.Equal(4, result.Count);
        Assert.Empty(result[1].Params!);

        var item = new WorkItem("77434", "data", "Script com chamado", null, WorkItemOrigin.Specialist, null, null, result, null, "Ana", Now);
        Assert.Equal(["sql-script", "ticket", "ticket", "sql-query"], item.OrderedTasks.Select(t => t.Kind));
        Assert.Equal(["claude", "claude", "user", "claude"], item.OrderedTasks.Select(t => t.Executor));
    }

    [Fact]
    public void Script_without_check_afterwards_gets_the_validation_after_the_ticket()
    {
        var result = WorkItemTicketRule.Apply([T("sql-query", "Consulta antes"), T("sql-script", "Script"), T("comment", "Avisar")], "Freshservice");

        Assert.Equal(["sql-query", "sql-script", "ticket", "ticket", "sql-query", "comment"], result.Select(t => t.Kind));
        Assert.Equal(WorkItemTicketRule.ValidateTitle, result[4].Title);
        Assert.Equal("claude", result[4].Executor);
    }

    [Fact]
    public void Item_with_query_after_the_script_keeps_only_the_ticket_pair()
    {
        var result = WorkItemTicketRule.Apply([T("sql-script", "Script"), T("sql-query", "Conferir")], "Freshservice");
        Assert.Equal(["sql-script", "ticket", "ticket", "sql-query"], result.Select(t => t.Kind));
        Assert.Equal("Conferir", result[3].Title);
    }

    [Fact]
    public void Validation_is_left_out_when_only_the_pair_fits()
    {
        var tasks = Enumerable.Range(0, WorkItem.MaxTasks - 2).Select(i => T(i == 0 ? "sql-script" : "manual", $"t{i}", "user")).ToList();
        var result = WorkItemTicketRule.Apply(tasks, "Freshservice");
        Assert.Equal(WorkItem.MaxTasks, result.Count);
        Assert.DoesNotContain(result, t => t.Title == WorkItemTicketRule.ValidateTitle);
    }

    [Fact]
    public void Full_item_has_no_room_for_the_pair()
    {
        var tasks = Enumerable.Range(0, WorkItem.MaxTasks - 1).Select(i => T(i == 0 ? "sql-script" : "manual", $"t{i}", "user")).ToList();
        Assert.Same(tasks, WorkItemTicketRule.Apply(tasks, "Freshservice"));
    }
}
