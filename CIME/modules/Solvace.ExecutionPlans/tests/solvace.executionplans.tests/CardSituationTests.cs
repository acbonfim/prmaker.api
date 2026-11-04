using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0075: "Situação atual" do card — chamado, aprovação/merge, validação em QA — montada só com o plano.</summary>
public class CardSituationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
    private static bool IsQa(string? branch) => string.Equals(branch, "qa", StringComparison.OrdinalIgnoreCase);

    private static ExecutionPlan Correction(params ExecutionStepDefinition[] steps)
    {
        var plan = new ExecutionPlan("75812", "analisar-bug", "Corrigir filtro de data", null, Guid.NewGuid(), "Alex", Now, ExecutionPhase.Correction, Guid.NewGuid());
        plan.UpsertSteps(steps, Now);
        return plan;
    }

    private static ExecutionStepDefinition Step(string key, string kind = ExecutionStepKind.Task, string executor = ExecutionExecutor.Claude, params string[] dependsOn) =>
        new() { Key = key, Title = key, Kind = kind, Executor = executor, Repository = kind == ExecutionStepKind.PullRequest ? "edv-solvace" : null, DependsOn = [.. dependsOn] };

    private static ExecutionLink Pr(ExecutionPlan plan, string step, int number, string target, string status)
    {
        var link = new ExecutionLink(plan.Id, step, $"https://github.com/acbonfim/edv-solvace/pull/{number}", $"#{number} edv-solvace → {target}",
            ExecutionLinkKind.PullRequest, false, number, "edv-solvace", target, "PRMake", Now);
        if (status != ExecutionLinkStatus.Open) link.ChangeStatus(status, "GitHub", Now.AddHours(1));
        return link;
    }

    [Fact]
    public void Qa_pr_merged_with_validation_pending_is_qa_validation()
    {
        var plan = Correction(Step("corrigir"), Step("pr-edv-solvace", ExecutionStepKind.PullRequest, ExecutionExecutor.Claude, "corrigir"),
            Step("validar-qa", ExecutionStepKind.Validation, ExecutionExecutor.User, "pr-edv-solvace"),
            Step("fechar-card", ExecutionStepKind.Task, ExecutionExecutor.Claude, "validar-qa"));
        plan.CompleteStep("corrigir", "ok", "claude", Now, true);
        plan.CompleteStep("pr-edv-solvace", "2 PRs mesclados", "PRMake", Now, false);
        plan.RecordDevTestInQa(null, Now.AddHours(1));
        var links = new List<ExecutionLink> { Pr(plan, "pr-edv-solvace", 10, "development", ExecutionLinkStatus.Merged), Pr(plan, "pr-edv-solvace", 11, "qa", ExecutionLinkStatus.Merged) };

        var s = CardSituation.Build(plan, links, [], [], IsQa, plan.CardNumber);

        Assert.Equal(CardSituationKind.QaValidation, s.Kind);
        Assert.Equal("validar-qa", s.StepKey);
        Assert.True(s.PullRequests.Single(p => p.Number == 11).IsQa);
        Assert.Contains("Dev Test in QA", s.Markdown);
        Assert.Contains("Ready for QA", s.Next);
    }

    [Fact]
    public void Open_ticket_is_waiting_for_the_ticket_with_its_files()
    {
        var plan = Correction(Step("preparar-script"), Step("chamado-ajuste", ExecutionStepKind.Ticket, ExecutionExecutor.User, "preparar-script"));
        plan.CompleteStep("preparar-script", "ok", "claude", Now, true);
        var ticket = new ExecutionLink(plan.Id, "chamado-ajuste", "https://solvace.freshservice.com/helpdesk/tickets/123", "#123 Ajuste de datas",
            ExecutionLinkKind.Ticket, true, null, null, null, "Alex", Now);
        var files = new List<ExecutionArtifact>
        {
            Artifact(plan, "01_ajuste.sql", "chamado-ajuste"), Artifact(plan, "chamado-ajuste.md", "chamado-ajuste")
        };

        var s = CardSituation.Build(plan, [ticket], [], files, IsQa, plan.CardNumber);

        Assert.Equal(CardSituationKind.Ticket, s.Kind);
        Assert.Equal(["01_ajuste.sql", "chamado-ajuste.md"], s.Tickets.Single().Files);
        Assert.Contains("Resolvido", s.Next);
    }

    [Fact]
    public void Open_pr_is_waiting_for_review_with_approvals_and_ci()
    {
        var plan = Correction(Step("corrigir"), Step("pr-edv-solvace", ExecutionStepKind.PullRequest, ExecutionExecutor.Claude, "corrigir"));
        plan.CompleteStep("corrigir", "ok", "claude", Now, true);
        var pr = Pr(plan, "pr-edv-solvace", 12, "development", ExecutionLinkStatus.Open);
        pr.ChangeReviews(1, 0, ["revisor-a"]);
        pr.ChangeChecks(ExecutionLinkChecks.Pending, "abc", [], Now);

        var s = CardSituation.Build(plan, [pr], [], [], IsQa, plan.CardNumber);

        Assert.Equal(CardSituationKind.PullRequestReview, s.Kind);
        Assert.Contains("1 aprovação", s.Markdown);
        Assert.Contains("revisor-a", s.Markdown);
        Assert.Contains("CI ⏳", s.Markdown);
    }

    [Fact]
    public void Handoff_goes_into_the_summary()
    {
        var plan = Correction(Step("corrigir"));
        plan.SetHandoff("Se o chamado voltar recusado, revisar o filtro de datas.", "Claude", Now);

        var s = CardSituation.Build(plan, [], [], [], IsQa, plan.CardNumber);

        Assert.Contains("Contexto da passagem", s.Markdown);
        Assert.Contains("revisar o filtro de datas", s.Markdown);
    }

    [Fact]
    public void Finished_plan_has_nothing_waiting()
    {
        var plan = Correction(Step("corrigir"));
        plan.CompleteStep("corrigir", "ok", "claude", Now, true);
        plan.TryAutoComplete("claude", Now);

        Assert.Equal(CardSituationKind.None, CardSituation.Build(plan, [], [], [], IsQa, plan.CardNumber).Kind);
    }

    [Fact]
    public void Dev_test_in_qa_retries_at_most_every_ten_minutes_and_only_until_it_works()
    {
        var plan = Correction(Step("corrigir"));
        Assert.True(plan.DevTestInQaDue(Now));
        plan.RecordDevTestInQa("sem integração do Azure", Now);
        Assert.False(plan.DevTestInQaDue(Now.AddMinutes(5)));
        Assert.True(plan.DevTestInQaDue(Now.AddMinutes(10)));
        plan.RecordDevTestInQa(null, Now.AddMinutes(10));
        Assert.False(plan.DevTestInQaDue(Now.AddDays(1)));
        Assert.Null(CardSituation.Build(plan, [], [], [], IsQa, plan.CardNumber).DevTestInQaError);
    }

    private static ExecutionArtifact Artifact(ExecutionPlan plan, string name, string step)
    {
        var a = new ExecutionArtifact(plan.Id, name, null, "claude", Now, 1);
        a.SetContent([1], "text/plain", step, null, Now, true);
        return a;
    }
}
