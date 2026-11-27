using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 79047: a correção foi mesclada e publicada em QA, a validação achou problema e o especialista inseriu ajustes
/// antes de "Validar em QA" e abriu PRs novos. A validação não pode virar "aguardando você" enquanto o Claude ainda faz
/// os ajustes nem enquanto os PRs novos não forem mesclados, e a situação precisa contar o caminho até aqui.
/// </summary>
public class ReworkRound79047Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-27T13:00:00Z");
    private static bool IsQa(string? branch) => string.Equals(branch, "qa", StringComparison.OrdinalIgnoreCase);

    private static ExecutionStepDefinition Step(string key, string kind = ExecutionStepKind.Task, string executor = ExecutionExecutor.Claude, params string[] dependsOn) =>
        new() { Key = key, Title = key, Kind = kind, Executor = executor, Repository = kind == ExecutionStepKind.PullRequest ? "edv-solvace" : null, DependsOn = [.. dependsOn] };

    /// <summary>Plano como o do 79047 com a 1ª rodada mesclada e publicada em QA às 14:54 (UTC).</summary>
    private static ExecutionPlan Published()
    {
        var plan = new ExecutionPlan("79047", "analisar-bug", "Correcao 79047", null, Guid.NewGuid(), "Alex", Now, ExecutionPhase.Correction, Guid.NewGuid());
        plan.UpsertSteps([
            Step("corrigir"),
            Step("pr-edv-solvace", ExecutionStepKind.PullRequest, ExecutionExecutor.Claude, "corrigir"),
            Step("preparar-fechamento", ExecutionStepKind.Task, ExecutionExecutor.Claude, "pr-edv-solvace"),
            Step("validar-qa", ExecutionStepKind.Validation, ExecutionExecutor.User, "pr-edv-solvace", "preparar-fechamento"),
            Step("fechar-card", ExecutionStepKind.Task, ExecutionExecutor.Claude, "validar-qa")
        ], Now);
        plan.CompleteStep("corrigir", "ok", "claude", Now, true);
        plan.CompleteStep("pr-edv-solvace", "2 PRs mesclados", "PRMake", Now.AddHours(1), false);
        plan.CompleteStep("preparar-fechamento", "ok", "claude", Now.AddHours(1), true);
        plan.RecordDevTestInQa(null, Now.AddHours(1));
        return plan;
    }

    private static ExecutionLink Pr(ExecutionPlan plan, int number, string target, DateTimeOffset openedAt, DateTimeOffset? mergedAt = null)
    {
        var link = new ExecutionLink(plan.Id, "pr-edv-solvace", $"https://github.com/electradv/edv-solvace/pull/{number}", $"#{number} edv-solvace -> {target}",
            ExecutionLinkKind.PullRequest, false, number, "edv-solvace", target, "PRMake", openedAt);
        if (mergedAt is { } m) link.ChangeStatus(ExecutionLinkStatus.Merged, "GitHub", m);
        return link;
    }

    private static ExecutionStep Get(ExecutionPlan plan, string key) => plan.Steps.Single(s => s.Key == key);

    [Fact]
    public void Validation_is_your_turn_only_after_the_steps_put_before_it()
    {
        var plan = Published();
        Assert.Contains(plan.StepsPendingForUser(), s => s.Key == "validar-qa");

        plan.AddStep(new ExecutionStepDefinition { Key = "pedido-corrigir-sala", Title = "Corrigir sala nao encontrada", Executor = ExecutionExecutor.Claude, TaskKind = "code" },
            "validar-qa", Now.AddHours(4));

        Assert.Contains("pedido-corrigir-sala", plan.EffectiveDependsOn(Get(plan, "validar-qa")));
        Assert.DoesNotContain(plan.StepsPendingForUser(), s => s.Key == "validar-qa");
        Assert.Empty(plan.UserActions([]));

        plan.CompleteStep("pedido-corrigir-sala", "ok", "claude", Now.AddHours(4.5), true);
        Assert.DoesNotContain("pedido-corrigir-sala", plan.EffectiveDependsOn(Get(plan, "validar-qa")));
        Assert.Contains(plan.StepsPendingForUser(), s => s.Key == "validar-qa");
    }

    [Fact]
    public void Steps_after_the_validation_do_not_depend_on_it_through_the_pinned_rule()
    {
        var plan = Published();
        plan.AddStep(new ExecutionStepDefinition { Key = "pedido-x", Title = "x", Executor = ExecutionExecutor.Claude }, "validar-qa", Now.AddHours(4));

        // As etapas antes da inserida não ganham dependência dela.
        Assert.DoesNotContain("pedido-x", plan.EffectiveDependsOn(Get(plan, "corrigir")));
        Assert.Contains("pedido-x", plan.EffectiveDependsOn(Get(plan, "fechar-card")));
    }

    [Fact]
    public void New_open_pr_on_a_completed_pr_step_waits_for_the_merge_again()
    {
        var plan = Published();
        var links = new List<ExecutionLink>
        {
            Pr(plan, 31429, "development", Now.AddMinutes(10), Now.AddHours(1)),
            Pr(plan, 31430, "qa", Now.AddMinutes(10), Now.AddHours(1)),
            Pr(plan, 31443, "qa", Now.AddHours(4))
        };

        var step = plan.WaitForPullRequests("pr-edv-solvace", links, "PRMake", Now.AddHours(4), false, includeRunning: true);

        Assert.NotNull(step);
        Assert.Equal(ExecutionStatus.Waiting, step!.Status);
        Assert.Equal(ExecutionWaitingOn.External, step.WaitingOn);
        Assert.Contains("#31443", step.StatusReason);
        Assert.DoesNotContain(plan.StepsPendingForUser(), s => s.Key == "validar-qa");

        links[2].ChangeStatus(ExecutionLinkStatus.Merged, "GitHub", Now.AddHours(5));
        plan.CompleteStep("pr-edv-solvace", "3 PRs mesclados", "PRMake", Now.AddHours(5), false);
        Assert.Contains(plan.StepsPendingForUser(), s => s.Key == "validar-qa");
    }

    [Fact]
    public void Pr_left_open_before_the_step_was_completed_does_not_reopen_it()
    {
        var plan = Published();
        // Aberto antes de a etapa concluir (a pessoa concluiu à mão com o PR aberto): não é rodada nova.
        var links = new List<ExecutionLink> { Pr(plan, 31429, "development", Now.AddMinutes(10)) };

        Assert.Null(plan.WaitForPullRequests("pr-edv-solvace", links, "PRMake", Now.AddHours(4), false, includeRunning: true));
        Assert.Equal(ExecutionStatus.Completed, Get(plan, "pr-edv-solvace").Status);
    }

    [Fact]
    public void Situation_tells_the_path_so_far_when_the_correction_came_back()
    {
        var plan = Published();
        var asked = plan.AddStep(new ExecutionStepDefinition { Key = "pedido-corrigir-sala", Title = "Corrigir \"sala nao encontrada\" no RoomPage", Executor = ExecutionExecutor.Claude, TaskKind = "code" },
            "validar-qa", Now.AddHours(4));
        plan.AddStep(new ExecutionStepDefinition { Key = "pedido-abrir-prs-novos", Title = "Abrir PRs novos", Executor = ExecutionExecutor.Claude, TaskKind = "pr" },
            "validar-qa", Now.AddHours(4));
        plan.UpdateStep(asked.Key, ExecutionStatus.Running, null, null, null, null, null, "claude", Now.AddHours(4), null);
        plan.CompleteStep(asked.Key, "ok", "claude", Now.AddHours(4.1), true);
        plan.CompleteStep("pedido-abrir-prs-novos", "ok", "claude", Now.AddHours(4.2), true);
        var links = new List<ExecutionLink>
        {
            Pr(plan, 31429, "development", Now.AddMinutes(10), Now.AddHours(1)),
            Pr(plan, 31430, "qa", Now.AddMinutes(10), Now.AddHours(1)),
            Pr(plan, 31443, "qa", Now.AddHours(4.2)),
            Pr(plan, 31444, "development", Now.AddHours(4.2))
        };
        plan.WaitForPullRequests("pr-edv-solvace", links, "PRMake", Now.AddHours(4.2), false, includeRunning: true);

        var s = CardSituation.Build(plan, links, [], [], IsQa, plan.CardNumber);

        Assert.Equal(CardSituationKind.PullRequestReview, s.Kind);
        Assert.True(s.Rework);
        Assert.StartsWith("**Aguardando revisão e merge dos PRs**", s.Markdown);
        Assert.Contains("Os PRs do ajuste estão abertos desde", s.Markdown);
        Assert.DoesNotContain("Já mesclados", s.Markdown);
        Assert.Contains("**Até aqui**", s.Markdown);
        Assert.Contains("A correção foi nos PRs [#31429]", s.Markdown);
        Assert.Contains("publicada em QA (card em Dev Test in QA desde", s.Markdown);
        Assert.Contains("Depois da publicação em QA, a correção voltou para ajuste: corrigir \"sala nao encontrada\" no RoomPage.", s.Markdown);
        Assert.DoesNotContain("abrir PRs novos", s.Markdown);
        Assert.Contains("O ajuste está nos PRs [#31443]", s.Markdown);
        Assert.Contains("volto a validar em QA", s.Next);
    }

    [Fact]
    public void Single_round_has_no_history()
    {
        var plan = Published();
        var links = new List<ExecutionLink> { Pr(plan, 31429, "development", Now.AddMinutes(10), Now.AddHours(1)), Pr(plan, 31430, "qa", Now.AddMinutes(10), Now.AddHours(1)) };

        var s = CardSituation.Build(plan, links, [], [], IsQa, plan.CardNumber);

        Assert.Equal(CardSituationKind.QaValidation, s.Kind);
        Assert.False(s.Rework);
        Assert.Empty(s.History);
        Assert.DoesNotContain("Até aqui", s.Markdown);
    }

    [Fact]
    public void Mirror_task_of_the_validation_depends_on_the_inserted_steps()
    {
        var plan = Published();
        plan.AddStep(new ExecutionStepDefinition { Key = "pedido-corrigir-sala", Title = "Corrigir", Executor = ExecutionExecutor.Claude }, "validar-qa", Now.AddHours(4));
        var item = new WorkItem("79047", WorkItemType.Fix, "Correcao 79047", null, WorkItemOrigin.Correction, null, plan.Id,
            plan.Steps.OrderBy(s => s.Order).Select(s => new WorkTaskInput { Title = s.Title, Kind = WorkTaskKind.Manual, StepKey = s.Key }).ToList(),
            Guid.NewGuid(), "Alex", Now);
        var n = 1187L;
        foreach (var t in item.OrderedTasks)
            typeof(WorkItemTask).GetProperty(nameof(WorkItemTask.Number))!.SetValue(t, ++n);
        var validation = Get(plan, "validar-qa");

        item.SyncStepControl(validation.Key, validation.Held, validation.HeldBy, plan.EffectiveDependsOn(validation), Now);

        var task = item.Tasks.Single(t => t.StepKey == "validar-qa");
        var inserted = item.Tasks.Single(t => t.StepKey == "pedido-corrigir-sala");
        Assert.Contains(inserted.Number, task.DependsOn);
    }
}
