using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using solvace.governance.application;
using solvace.governance.domain.Entities;
using solvace.prform.Governance;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0098: contas das métricas (mediana, p90, marcos, linha do tempo) e a ação de uma requisição HTTP.</summary>
public class GovernanceMetricsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Median_and_p90_interpolate_like_postgres()
    {
        Assert.Null(GovernanceMetricsMath.Median([]));
        Assert.Equal(20, GovernanceMetricsMath.Median([30, 10, 20]));
        Assert.Equal(25, GovernanceMetricsMath.Median([10, 20, 30, 40]));
        Assert.Equal(91, GovernanceMetricsMath.Percentile(Enumerable.Range(1, 101).Select(i => (long)i - 1), 0.9) + 1);
    }

    [Theory]
    [InlineData("03-correcao", "correcao")]
    [InlineData("correcao-2", "correcao")]
    [InlineData("analise-inicial", "analise-inicial")]
    [InlineData("01_prototipo-solucao", "prototipo-solucao")]
    public void Step_keys_lose_order_numbers(string key, string expected) =>
        Assert.Equal(expected, GovernanceMetricsMath.NormalizeStepKey(key));

    private static CardActivity Activity()
    {
        var analysis = new PlanRow(Guid.NewGuid(), "78720", "analisar-bug", "analysis", "completed", "Erro", null, "Ana", T0, T0.AddMinutes(5), T0.AddMinutes(50));
        var correction = new PlanRow(Guid.NewGuid(), "78720", "analisar-bug", "correction", "running", "Erro", null, "Ana", T0.AddDays(1), T0.AddDays(1), null);
        var item = new WorkItemRow(Guid.NewGuid(), 1001, "78720", "fix", "Correção", "running", null, "Ana", T0.AddDays(1), T0.AddDays(1), null);
        return new CardActivity(78720, [analysis, correction],
            [
                new StepRow(analysis.Id, "prototipo-solucao", "task", "Protótipo", "completed", "Ana", T0.AddMinutes(30), T0.AddMinutes(45)),
                new StepRow(correction.Id, "03-correcao", "code", "Correção", "running", "Ana", T0.AddDays(1), null)
            ],
            [item],
            [new TaskRow(item.Id, 2, "pr", "Abrir PR", "completed", T0.AddDays(1).AddHours(1), T0.AddDays(1).AddHours(1).AddMinutes(10))],
            [],
            [new EventRow(1, T0.AddDays(2), 120, null, "Ana", "screen", "POST Azure/card/{id}/actions/ready-for-qa", "devops", "ok", [], null)]);
    }

    [Fact]
    public void Default_milestones_come_from_plans_steps_tasks_and_events()
    {
        var hits = GovernanceMetricsMath.Milestones(Activity(), GovernanceSettings.DefaultMilestones()).ToDictionary(h => h.Key, h => h.At);
        Assert.Equal(T0, hits["task-started"]);
        Assert.Equal(T0.AddMinutes(5), hits["analysis-started"]);
        Assert.Equal(T0.AddMinutes(45), hits["prototype"]);
        Assert.Equal(T0.AddMinutes(50), hits["analysis-done"]);
        Assert.Equal(T0.AddDays(1), hits["correction-started"]);
        Assert.Equal(T0.AddDays(1).AddHours(1).AddMinutes(10), hits["pr-opened"]);
        Assert.Equal(T0.AddDays(2), hits["qa"]);
        Assert.False(hits.ContainsKey("closed")); // correção ainda rodando
    }

    [Fact]
    public void Timeline_is_chronological_with_durations()
    {
        var a = Activity();
        var rows = GovernanceMetricsMath.Timeline(a, GovernanceMetricsMath.Milestones(a, GovernanceSettings.DefaultMilestones()));
        Assert.Equal(rows.OrderBy(r => r.At).Select(r => r.At), rows.Select(r => r.At));
        var prototype = rows.Single(r => r.Kind == "step" && r.Ref == "prototipo-solucao");
        Assert.Equal(15 * 60_000, prototype.DurationMs);
        Assert.Contains(rows, r => r.Kind == "milestone" && r.Ref == "pr-opened");
    }

    [Fact]
    public void Http_action_keeps_verbs_and_hides_ids()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.RouteValues = new RouteValueDictionary
        {
            ["controller"] = "WorkItems", ["version"] = "1", ["number"] = "1001", ["taskNumber"] = "2", ["verb"] = "complete"
        };
        http.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/v{version:apiVersion}/WorkItems/{number:long}/tasks/{taskNumber:long}/{verb}"), 0, null, null));
        var info = GovernanceRoute.Describe(http);
        Assert.Equal("POST WorkItems/{number}/tasks/{taskNumber}/complete", info.Action);
        Assert.Equal("task", info.Category);
        Assert.Equal(1001, info.WorkItem);
        Assert.Equal(2, info.Task);
    }

    [Fact]
    public void Azure_card_id_is_the_card()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.RouteValues = new RouteValueDictionary { ["controller"] = "Azure", ["version"] = "1", ["id"] = "78720" };
        http.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/v{version:apiVersion}/Azure/card/{id}/actions/ready-for-qa"), 0, null, null));
        var info = GovernanceRoute.Describe(http);
        Assert.Equal("POST Azure/card/{id}/actions/ready-for-qa", info.Action);
        Assert.Equal(78720, info.Card);
        Assert.Equal("devops", info.Category);
    }

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "POST Timeline" }, false)]
    [InlineData(new[] { "mcp:prmake_pr_*" }, false)]
    [InlineData(new[] { "hook:PreToolUse:*" }, true)]
    [InlineData(new[] { "*reset" }, true)]
    public void Only_rules_that_can_match_a_hook_go_to_the_hook_matchers(string[] actions, bool expected) =>
        Assert.Equal(expected, GovernanceApplication.CanMatchHook(new RuleMatch { Actions = [.. actions], Patterns = ["x"] }));
}

/// <summary>0098: o prmake_rules lista os guardrails ativos da governança (o que o hook e o servidor vão aplicar).</summary>
public class GovernanceRulesMarkdownTests
{
    [Fact]
    public void Lists_active_non_monitor_rules_and_nothing_when_off()
    {
        var now = DateTimeOffset.UtcNow;
        var rules = solvace.governance.domain.GovernanceDefaults.Rules.Select(d => solvace.governance.domain.GovernanceDefaults.CreateRule(d, now)).ToList();
        var md = solvace.prform.Skills.SkillsRulesService.GovernanceMarkdown("enforce", rules);
        Assert.Contains("## Governança (modo aplicando)", md);
        Assert.Contains("`no-merge`", md);
        Assert.DoesNotContain("`config-changes-audited`", md); // só monitora
        Assert.Equal(string.Empty, solvace.prform.Skills.SkillsRulesService.GovernanceMarkdown("off", rules));
    }
}
