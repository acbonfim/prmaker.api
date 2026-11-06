using System.Text;
using System.Text.Json;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// 0091: entregas do item — montagem (gravadas + deduzidas de output/links, links da etapa do plano e PRs do card), chave
/// da gravação, corte do diff e leitura do <c>git diff</c>.
/// </summary>
public class WorkItemDeliverableTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-06T14:00:00Z");

    private static WorkItem Item(Guid? planId, params WorkTaskInput[] tasks)
    {
        var item = new WorkItem("77988", WorkItemType.Implementation, "Implementação 77988", null,
            planId is null ? WorkItemOrigin.User : WorkItemOrigin.Correction, null, planId, tasks, Guid.NewGuid(), "Ana", Now);
        var n = 56L;
        typeof(WorkItem).GetProperty(nameof(WorkItem.Number))!.SetValue(item, n);
        foreach (var t in item.Tasks.OrderBy(t => t.Order))
            typeof(WorkItemTask).GetProperty(nameof(WorkItemTask.Number))!.SetValue(t, ++n);
        return item;
    }

    private static WorkTaskInput Task(string kind, string? step = null, string? repo = null) => new()
    {
        Title = $"{kind} {repo}", Kind = kind, StepKey = step,
        Params = repo is null ? null : new Dictionary<string, string> { ["repository"] = repo },
    };

    private static long NumberOf(WorkItem item, string kind, string? repo = null) =>
        item.Tasks.First(t => t.Kind == kind && (repo is null || t.Params.GetValueOrDefault("repository") == repo)).Number;

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o);

    // ---------------------------------------------------------------- montagem retroativa

    [Fact]
    public void Plan_mirror_item_gets_the_pr_of_the_step_link_and_the_card_pr_opened_while_the_task_ran()
    {
        var planId = Guid.NewGuid();
        var item = Item(planId,
            Task(WorkTaskKind.Code, "implementar-edv-solvace", "edv-solvace"),
            Task(WorkTaskKind.PullRequest, "pr-edv-solvace", "edv-solvace"),
            Task(WorkTaskKind.PullRequest, "pr-revamp-users", "revamp-Users"));
        item.SyncStep("pr-revamp-users", ExecutionStatus.Running, null, "Plano", Now);

        var link = new ExecutionLink(planId, "pr-edv-solvace", "https://github.com/solvace/edv-solvace/pull/4521", null,
            ExecutionLinkKind.PullRequest, false, 4521, "edv-solvace", "development", "Claude", Now);
        var cardPrs = new[]
        {
            new DeliverableCardPr("edv-solvace", 4521, "https://github.com/solvace/edv-solvace/pull/4521", "development", "feature/77988-dev", "merged", Now),
            new DeliverableCardPr("revamp-Users", 88, "https://github.com/solvace/revamp-Users/pull/88", "development", "feature/77988-dev", "open", Now.AddMinutes(1)),
            // Aberto muito antes da sub-tarefa: é de outro momento do card, não entra.
            new DeliverableCardPr("revamp-Users", 70, "https://github.com/solvace/revamp-Users/pull/70", "qa", "feature/77988-qa", "merged", Now.AddDays(-3)),
        };

        var list = WorkDeliverableListing.Build(item, [], [link], cardPrs);

        Assert.Equal(2, list.Count);
        var edv = list[0];
        Assert.Equal(NumberOf(item, WorkTaskKind.PullRequest, "edv-solvace"), edv.TaskNumber);
        Assert.Equal(WorkDeliverableSource.Plan, edv.Source);
        Assert.Equal("feature/77988-dev", edv.Branch);
        Assert.True(edv.HasDiff);

        var users = list[1];
        Assert.Equal(NumberOf(item, WorkTaskKind.PullRequest, "revamp-Users"), users.TaskNumber);
        Assert.Equal(WorkDeliverableSource.Deduced, users.Source);
        Assert.Equal(88, users.Number);
        Assert.Equal("open", users.Status);
    }

    [Fact]
    public void Task_output_and_item_link_of_the_same_pr_become_one_deliverable()
    {
        var item = Item(null, Task(WorkTaskKind.PullRequest, repo: "edv-solvace"));
        var task = item.Tasks.Single();
        item.ApplyTaskAction(task.Number, WorkTaskAction.Complete, "PR aberto",
            """{"pr":"https://github.com/solvace/edv-solvace/pull/9","number":"9","repository":"edv-solvace","branch":"hotfix/77434-rv","target":"release-version","alreadyExisted":true}""",
            null, "Claude", Now);
        item.AddLinks([new WorkItemLink { Kind = WorkItemLinkKind.PullRequest, Title = "PR #9", Url = "https://github.com/solvace/edv-solvace/pull/9/" }], Now);

        var list = WorkDeliverableListing.Build(item, [], [], []);

        var pr = Assert.Single(list);
        Assert.Equal(WorkDeliverableKind.PullRequest, pr.Kind);
        Assert.Equal(task.Number, pr.TaskNumber);
        Assert.Equal(9, pr.Number);
        Assert.Equal("hotfix/77434-rv", pr.Branch);
        Assert.Equal("release-version", pr.Target);
        Assert.True(pr.Data.GetProperty("alreadyExisted").GetBoolean());
    }

    [Fact]
    public void Stored_code_wins_over_the_branch_of_the_output_and_reverted_task_marks_it()
    {
        var item = Item(null, Task(WorkTaskKind.Code, repo: "edv-solvace"));
        var task = item.Tasks.Single();
        item.ApplyTaskAction(task.Number, WorkTaskAction.Complete, "ok", """{"branch":"feature/77988","commit":"abc1234"}""", null, "Claude", Now);
        var stored = new WorkItemDeliverable("77988", item.Id, task.Number, new WorkDeliverableInput
        {
            Kind = "code", Repository = "edv-solvace", Branch = "feature/77988", Target = "development", Sha = "abc1234",
        }, WorkDeliverableSource.Task, "Claude", Now);
        stored.SetPatch(Encoding.UTF8.GetBytes("diff --git a/x.cs b/x.cs\n@@ -1 +1 @@\n-a\n+b\n"));

        var list = WorkDeliverableListing.Build(item, [stored], [], []);
        var code = Assert.Single(list);
        Assert.Equal(stored.Id.ToString(), code.Id);
        Assert.True(code.HasDiff);
        Assert.False(code.Reverted);

        item.StartUndo("Ana", Now);
        item.ApplyTaskAction(task.Number, WorkTaskAction.Revert, "revertido", null, null, "Claude", Now);
        Assert.True(WorkDeliverableListing.Build(item, [stored], [], []).Single().Reverted);
    }

    [Fact]
    public void Output_of_devops_ticket_and_hours_tasks_become_deliverables()
    {
        var item = Item(null, Task(WorkTaskKind.DevOpsState), Task(WorkTaskKind.Ticket), Task(WorkTaskKind.Notify));
        var tasks = item.Tasks.OrderBy(t => t.Order).ToList();
        item.ApplyTaskAction(tasks[0].Number, WorkTaskAction.Complete, null, """{"from":"Active","to":"Resolved","action":"set-state"}""", null, "Claude", Now);
        item.ApplyTaskAction(tasks[1].Number, WorkTaskAction.Complete, null, """{"system":"Freshservice","ticket":"https://solvace.freshservice.com/a/tickets/123"}""", null, "Ana", Now);
        item.ApplyTaskAction(tasks[2].Number, WorkTaskAction.Complete, null, """{"where":"timeline"}""", null, "Claude", Now);

        var list = WorkDeliverableListing.Build(item, [], [], []);

        Assert.Equal([WorkDeliverableKind.DevOpsState, WorkDeliverableKind.Ticket], list.Select(d => d.Kind).ToArray());
        Assert.Equal("Card movido de Active para Resolved", list[0].Title);
        Assert.Equal("Freshservice", list[1].Data.GetProperty("system").GetString());
        Assert.Equal("https://solvace.freshservice.com/a/tickets/123", list[1].Url);
    }

    // ---------------------------------------------------------------- gravação

    [Fact]
    public void Key_identifies_pr_by_repository_and_number_and_events_are_always_new()
    {
        var pr = WorkItemDeliverable.KeyOf("pr", new WorkDeliverableInput { Repository = "EDV-solvace", Number = 12, Url = "https://x/pull/12" });
        Assert.Equal("pr:edv-solvace#12", pr);
        Assert.Equal("code:edv-solvace:feature/1", WorkItemDeliverable.KeyOf("code", new WorkDeliverableInput { Repository = "edv-solvace", Branch = "Feature/1" }));
        var a = WorkItemDeliverable.KeyOf("timeline", new WorkDeliverableInput { Text = "x" });
        var b = WorkItemDeliverable.KeyOf("timeline", new WorkDeliverableInput { Text = "x" });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Apply_keeps_values_that_did_not_come_and_kind_is_validated()
    {
        var d = new WorkItemDeliverable("77988", Guid.NewGuid(), 63, new WorkDeliverableInput
        {
            Kind = "pull-request", Repository = "edv-solvace", Number = 12, Branch = "feature/1", Target = "development",
        }, WorkDeliverableSource.Auto, "Claude", Now);
        Assert.Equal(WorkDeliverableKind.PullRequest, d.Kind);
        Assert.Equal("PR #12 edv-solvace: feature/1 → development", d.Title);

        d.Apply(new WorkDeliverableInput { Status = "MERGED" }, Now.AddHours(1));
        Assert.Equal("merged", d.Status);
        Assert.Equal("feature/1", d.Branch);
        Assert.Equal(12, d.Number);

        Assert.Throws<DomainException>(() => WorkDeliverableKind.Normalize("foguete"));
        Assert.True(WorkDeliverableKind.Fits(WorkDeliverableKind.PullRequest, WorkTaskKind.PullRequest));
        Assert.False(WorkDeliverableKind.Fits(WorkDeliverableKind.PullRequest, WorkTaskKind.Code));
    }

    [Fact]
    public void Big_patch_is_cut_at_the_last_line_break_before_the_limit()
    {
        var d = new WorkItemDeliverable("77988", Guid.NewGuid(), 58, new WorkDeliverableInput { Kind = "code", Repository = "r", Branch = "b" },
            WorkDeliverableSource.Task, "Claude", Now);
        var line = Encoding.UTF8.GetBytes(new string('+', 99) + "\n");
        var patch = Enumerable.Repeat(line, WorkItemDeliverable.MaxPatchBytes / 100 + 10).SelectMany(x => x).ToArray();

        Assert.True(d.SetPatch(patch));
        Assert.True(d.HasPatch);
        Assert.True(d.PatchTruncated);
        Assert.True(d.Content!.Data.Length <= WorkItemDeliverable.MaxPatchBytes);
        Assert.Equal((byte)'\n', d.Content.Data[^1]);
    }

    // ---------------------------------------------------------------- git diff

    [Fact]
    public void Unified_patch_reads_files_status_and_counts()
    {
        const string patch = """
            diff --git a/src/Query.cs b/src/Query.cs
            index 111..222 100644
            --- a/src/Query.cs
            +++ b/src/Query.cs
            @@ -24,3 +24,4 @@ class Q
             var a = 1;
            -QuerySingleOrDefault();
            +QueryFirstOrDefaultAsync();
            +// top 1
            diff --git a/Migrations/2026_77988.cs b/Migrations/2026_77988.cs
            new file mode 100644
            index 000..333
            --- /dev/null
            +++ b/Migrations/2026_77988.cs
            @@ -0,0 +1,2 @@
            +up
            +down
            diff --git a/old/Name.cs b/new/Name.cs
            similarity index 90%
            rename from old/Name.cs
            rename to new/Name.cs
            diff --git a/logo.png b/logo.png
            deleted file mode 100644
            Binary files a/logo.png and /dev/null differ
            """;

        var files = UnifiedPatch.Parse(patch);

        Assert.Equal(["src/Query.cs", "Migrations/2026_77988.cs", "new/Name.cs", "logo.png"], files.Select(f => f.Path).ToArray());
        Assert.Equal(["modified", "added", "renamed", "removed"], files.Select(f => f.Status).ToArray());
        Assert.Equal((2, 1), (files[0].Additions, files[0].Deletions));
        Assert.StartsWith("@@ -24,3 +24,4 @@", files[0].Patch);
        Assert.Equal((2, 0), (files[1].Additions, files[1].Deletions));
        Assert.Equal("old/Name.cs", files[2].PreviousPath);
        Assert.Null(files[2].Patch);
        Assert.Null(files[3].Patch);
    }
}
