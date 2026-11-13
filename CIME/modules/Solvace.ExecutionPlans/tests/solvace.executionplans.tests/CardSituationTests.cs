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
        Assert.StartsWith("**Aguardando validação em QA**", s.Markdown);
        Assert.Contains("O card está em Dev Test in QA desde", s.Markdown);
        Assert.Contains("PRs mesclados: [#10](https://github.com/acbonfim/edv-solvace/pull/10) (development) e [#11](https://github.com/acbonfim/edv-solvace/pull/11) (qa).", s.Markdown);
        Assert.DoesNotContain("Plano de correção", s.Markdown);
        Assert.DoesNotContain("🧪", s.Markdown);
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
        Assert.Contains("quando o chamado for atendido", s.Next, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRMake", s.Next);
        Assert.Contains("com os arquivos `01_ajuste.sql` e `chamado-ajuste.md`", s.Markdown);
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
        Assert.Contains("- [#12](https://github.com/acbonfim/edv-solvace/pull/12) (development): 1 aprovação, aguardando revisão de revisor-a, CI rodando.", s.Markdown);
    }

    [Fact]
    public void Handoff_goes_into_the_summary()
    {
        var plan = Correction(Step("corrigir"));
        plan.SetHandoff("Se o chamado voltar recusado, revisar o filtro de datas.", "Claude", Now);

        var s = CardSituation.Build(plan, [], [], [], IsQa, plan.CardNumber);

        Assert.Contains("**Contexto**", s.Markdown);
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

    [Fact]
    public void Failed_automatic_move_tells_to_move_by_the_devops_menu()
    {
        var plan = Correction(Step("pr-edv-solvace", ExecutionStepKind.PullRequest), Step("validar-qa", ExecutionStepKind.Validation, ExecutionExecutor.User, "pr-edv-solvace"));
        plan.CompleteStep("pr-edv-solvace", "ok", "PRMake", Now, false);
        plan.RecordDevTestInQa("sem integração", Now);
        var s = CardSituation.Build(plan, [Pr(plan, "pr-edv-solvace", 11, "qa", ExecutionLinkStatus.Merged)], [], [], IsQa, plan.CardNumber);
        Assert.StartsWith("Movo o card para Dev Test in QA", s.Next);
        Assert.Contains("Não foi possível mover o card para Dev Test in QA automaticamente (sem integração)", s.Markdown);
    }

    private static ExecutionArtifact Artifact(ExecutionPlan plan, string name, string step)
    {
        var a = new ExecutionArtifact(plan.Id, name, null, "claude", Now, 1);
        a.SetContent([1], "text/plain", step, null, Now, true);
        return a;
    }

    [Fact]
    public void Discussion_html_is_plain_and_escaped()
    {
        var html = SituationHtml.ToHtml("**Aguardando validação em QA**\n\nA correção foi publicada em QA.\n\n- [#11](https://github.com/x/y/pull/11) (qa)\n- veja [sa3.asp:10](ref:sa3-init) <script>\n\n**Próximo passo:** validar.");
        Assert.Equal("<p><b>Aguardando validação em QA</b></p><p>A correção foi publicada em QA.</p><ul><li><a href=\"https://github.com/x/y/pull/11\">#11</a> (qa)</li><li>veja <code>sa3.asp:10</code> &lt;script&gt;</li></ul><p><b>Próximo passo:</b> validar.</p>", html);
    }

    [Theory]
    [InlineData(CardSituationKind.QaValidation, false, true)]
    [InlineData(CardSituationKind.Ticket, false, true)]
    [InlineData(CardSituationKind.None, false, false)]
    [InlineData(CardSituationKind.None, true, true)]
    [InlineData(CardSituationKind.Answer, false, false)]
    public void Posts_to_devops_on_waits_and_keeps_the_comment_updated(string kind, bool hasComment, bool expected) =>
        Assert.Equal(expected, CardSituationKind.PostToDevOps(new CardSituationResponse { Kind = kind }, hasComment));

    [Fact]
    public void Planned_user_step_in_the_correction_is_published_but_a_skill_block_is_not()
    {
        var plan = Correction(Step("configurar-multilingual", ExecutionStepKind.Task, ExecutionExecutor.User),
            Step("fechar-card", ExecutionStepKind.Task, ExecutionExecutor.Claude, "configurar-multilingual"));
        plan.UpdateStep("configurar-multilingual", ExecutionStatus.Waiting, "Restaurar Delayed — passo a passo em configuracao.md", null, null, null, null, "claude", Now, ExecutionWaitingOn.User);
        var task = CardSituation.Build(plan, [], [], [], IsQa, plan.CardNumber);
        Assert.Equal(CardSituationKind.User, task.Kind);
        Assert.True(task.PlannedTask);
        Assert.True(CardSituationKind.PostToDevOps(task, false));
        Assert.StartsWith("**Aguardando: configurar-multilingual**", task.Markdown);

        var blocked = Correction(Step("corrigir"));
        blocked.UpdateStep("corrigir", ExecutionStatus.Waiting, "Libere a permissão do Bash no Claude Code", null, null, null, null, "claude", Now, ExecutionWaitingOn.User);
        var block = CardSituation.Build(blocked, [], [], [], IsQa, blocked.CardNumber);
        Assert.Equal(CardSituationKind.User, block.Kind);
        Assert.False(block.PlannedTask);
        Assert.False(CardSituationKind.PostToDevOps(block, false));
    }
    // ── Chamado a abrir, QA depois do chamado e contexto velho (77988) ─────────────────────────────

    private static ExecutionPlan TicketPlan()
    {
        var plan = Correction(Step("preparar-script"), Step("chamado-limpar", ExecutionStepKind.Ticket, ExecutionExecutor.User, "preparar-script"),
            Step("validar-chamado-limpar", ExecutionStepKind.Validation, ExecutionExecutor.Claude, "chamado-limpar"),
            Step("pr-edv-solvace", ExecutionStepKind.PullRequest, ExecutionExecutor.Claude, "preparar-script"),
            Step("validar-qa", ExecutionStepKind.Validation, ExecutionExecutor.User, "pr-edv-solvace", "chamado-limpar", "validar-chamado-limpar"));
        plan.CompleteStep("preparar-script", "ok", "claude", Now, true);
        plan.CompleteStep("pr-edv-solvace", "PR mesclado", "PRMake", Now, false);
        return plan;
    }

    private static ExecutionLink Ticket(ExecutionPlan plan, string step) =>
        new(plan.Id, step, "https://solvace.freshservice.com/public/tickets/600", null, ExecutionLinkKind.Ticket, true, null, null, null, "Alex", Now);

    private static void Situation(ExecutionPlan plan, string kind, DateTimeOffset at)
    {
        typeof(ExecutionPlan).GetProperty(nameof(ExecutionPlan.SituationKind))!.SetValue(plan, kind);
        typeof(ExecutionPlan).GetProperty(nameof(ExecutionPlan.SituationAt))!.SetValue(plan, at);
    }

    [Fact]
    public void Ticket_step_without_ticket_is_waiting_to_open_the_ticket()
    {
        var plan = TicketPlan();
        var files = new List<ExecutionArtifact> { Artifact(plan, "chamado-limpar.md", "chamado-limpar"), Artifact(plan, "01_limpar.sql", "chamado-limpar") };

        var s = CardSituation.Build(plan, [Pr(plan, "pr-edv-solvace", 11, "qa", ExecutionLinkStatus.Merged)], [], files, IsQa, plan.CardNumber);

        Assert.Equal(CardSituationKind.TicketToOpen, s.Kind);
        Assert.Equal("Aguardando abrir chamado", s.Title);
        Assert.Equal("chamado-limpar", s.StepKey);
        Assert.StartsWith("**Aguardando abrir chamado**", s.Markdown);
        Assert.Contains("Arquivos do chamado no plano: `01_limpar.sql` e `chamado-limpar.md`.", s.Markdown);
        Assert.Empty(s.UserSteps);
        Assert.DoesNotContain("você", s.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.True(CardSituationKind.Milestone(s));
        // QA depende do chamado: vem depois, como "também".
        Assert.Equal(["Validar em QA"], s.AlsoWaiting);
    }

    [Fact]
    public void Attached_ticket_comes_before_qa_that_depends_on_it()
    {
        var plan = TicketPlan();
        var ticket = Ticket(plan, "chamado-limpar");
        plan.ApplyTicketState("chamado-limpar", [ticket], "Alex", Now, false);

        var s = CardSituation.Build(plan, [ticket, Pr(plan, "pr-edv-solvace", 11, "qa", ExecutionLinkStatus.Merged)], [], [], IsQa, plan.CardNumber);

        Assert.Equal(CardSituationKind.Ticket, s.Kind);
        Assert.StartsWith("**Aguardando o atendimento do chamado**", s.Markdown);
        Assert.Equal(["Validar em QA"], s.AlsoWaiting);
    }

    [Fact]
    public void Qa_comes_first_once_everything_before_it_is_done()
    {
        var plan = TicketPlan();
        plan.CompleteStep("chamado-limpar", "Chamado resolvido", "Alex", Now, false);
        plan.CompleteStep("validar-chamado-limpar", "ok", "claude", Now, true);

        var s = CardSituation.Build(plan, [Pr(plan, "pr-edv-solvace", 11, "qa", ExecutionLinkStatus.Merged)], [], [], IsQa, plan.CardNumber);

        Assert.Equal(CardSituationKind.QaValidation, s.Kind);
        Assert.Empty(s.AlsoWaiting);
    }

    [Fact]
    public void Handoff_written_before_the_current_wait_is_left_out()
    {
        var plan = TicketPlan();
        var ticket = Ticket(plan, "chamado-limpar");
        plan.ApplyTicketState("chamado-limpar", [ticket], "Alex", Now, false);
        plan.SetHandoff("**Espera:** voce abrir o chamado de limpeza.", "Claude", Now.AddHours(-3));
        var links = new List<ExecutionLink> { ticket };

        // Situação registrada antes de entrar no chamado (o PRMake ainda não gravou a mudança): o contexto é velho.
        Situation(plan, CardSituationKind.TicketToOpen, Now.AddHours(-4));
        var s = CardSituation.Build(plan, links, [], [], IsQa, plan.CardNumber);
        Assert.Null(s.Handoff);
        Assert.DoesNotContain("voce abrir", s.Markdown);

        // Entrou no chamado depois do contexto: velho também.
        Situation(plan, CardSituationKind.Ticket, Now.AddHours(-1));
        Assert.Null(CardSituation.Build(plan, links, [], [], IsQa, plan.CardNumber).Handoff);

        // Contexto gravado na espera atual (ou até 10 min antes de entrar nela): fica.
        Situation(plan, CardSituationKind.Ticket, Now.AddHours(-3).AddMinutes(5));
        var fresh = CardSituation.Build(plan, links, [], [], IsQa, plan.CardNumber);
        Assert.NotNull(fresh.Handoff);
        Assert.Contains("**Contexto**", fresh.Markdown);
    }

    [Fact]
    public void Discussion_comment_reads_as_written_by_the_person()
    {
        var html = SituationHtml.Build("**Aguardando: Excluir os posts**\n\nFaço o que a etapa pede.", Now, CardSituation.Brasilia);

        Assert.DoesNotContain("PRMake", html);
        Assert.DoesNotContain("automaticamente", html);
    }
}
