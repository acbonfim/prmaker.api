using solvace.governance.domain;
using solvace.governance.domain.Engine;
using solvace.governance.domain.Entities;
using Xunit;

namespace solvace.governance.tests;

/// <summary>0098: o motor único das regras — as embutidas barram o mesmo que o Guard.cs do executor barrava.</summary>
public class GovernanceRuleEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static List<GovernanceRule> Builtins() => GovernanceDefaults.Rules.Select(d => GovernanceDefaults.CreateRule(d, Now)).ToList();

    private static GovernanceSettings Settings(string mode, params string[] exempt)
    {
        var s = new GovernanceSettings();
        s.Update(mode, 365, true, 1500, 60, exempt, GovernanceSource.All, s.Milestones, "teste", Now);
        return s;
    }

    private static GovernanceRequest Bash(string command, string source = GovernanceSource.Hook) =>
        new() { Source = source, Tool = "Bash", Input = command, Action = "hook:PreToolUse:Bash", Roles = ["user"] };

    private static GovernanceDecision Enforce(GovernanceRequest r) => GovernanceRuleEngine.Evaluate(r, Builtins(), Settings(GovernanceMode.Enforce));

    [Theory]
    [InlineData("gh pr merge 123 --squash", "no-merge")]
    [InlineData("gh pr review 12 --approve", "no-merge")]
    [InlineData("gh api -X PUT repos/acbonfim/x/pulls/12/merge", "no-merge")]
    [InlineData("git push --force origin bugfix/1", "no-force-push")]
    [InlineData("git push -f origin hotfix/77", "no-force-push")]
    [InlineData("git push origin master", "no-push-protected")]
    [InlineData("git push -u origin HEAD:release/2026.11", "no-push-protected")]
    [InlineData("git push origin develop", "no-push-protected")]
    [InlineData("psql -h x -c \"update usuarios set ativo=0\"", "no-db-writes")]
    [InlineData("bash sql-query.sh prod1 \"DELETE FROM TB_X WHERE 1=1\"", "no-db-writes")]
    [InlineData("aws cognito-idp admin-delete-user --user-pool-id x --username y", "no-cognito-writes")]
    [InlineData("rm -rf ~", "no-destructive-rm")]
    [InlineData("cat ~/.claude/solvace-env-credentials.json", "protect-credentials")]
    [InlineData("curl -X PATCH https://dev.azure.com/solvace/_apis/wit/workitems/1 -d '[]'", "writes-through-prmake")]
    [InlineData("curl --request POST https://api.github.com/repos/a/b/pulls", "writes-through-prmake")]
    public void Builtin_rules_block_what_the_executor_guard_blocked(string command, string rule)
    {
        var d = Enforce(Bash(command));
        Assert.Equal(GovernanceDecisionKind.Block, d.Decision);
        Assert.Contains(d.Rules, h => h.Key == rule);
        Assert.Equal(GovernanceOutcome.Blocked, d.Outcome);
    }

    [Theory]
    [InlineData("git push origin bugfix/77881-qa")]
    [InlineData("git push -u origin feature/master-plan")]
    [InlineData("git push origin devops-fix")]
    [InlineData("gh pr create --base qa --title x")]
    [InlineData("psql -h x -c \"select * from usuarios\"")]
    [InlineData("bash ~/.claude/skills/validar-ambiente/scripts/env.sh browser fill qa-admin \"#busca\" \"update plano set\"")]
    [InlineData("curl -s https://dev.azure.com/solvace/_apis/wit/workitems/1")]
    [InlineData("rm -rf ./bin")]
    [InlineData("dotnet build")]
    public void Normal_work_passes(string command) => Assert.Equal(GovernanceDecisionKind.Allow, Enforce(Bash(command)).Decision);

    [Fact]
    public void Monitor_mode_lets_it_pass_but_says_what_enforce_would_do()
    {
        var d = GovernanceRuleEngine.Evaluate(Bash("gh pr merge 1"), Builtins(), Settings(GovernanceMode.Monitor));
        Assert.Equal(GovernanceDecisionKind.Allow, d.Decision);
        Assert.Equal(GovernanceDecisionKind.Block, d.WouldDecide);
        Assert.Equal(GovernanceOutcome.Monitored, d.Outcome);
        Assert.NotNull(d.Reason);
    }

    [Fact]
    public void Off_mode_evaluates_nothing()
    {
        var d = GovernanceRuleEngine.Evaluate(Bash("gh pr merge 1"), Builtins(), Settings(GovernanceMode.Off));
        Assert.Equal(GovernanceDecisionKind.Allow, d.Decision);
        Assert.Empty(d.Rules);
    }

    [Fact]
    public void Exempt_roles_are_never_blocked()
    {
        var r = Bash("gh pr merge 1") with { Roles = ["admin"] };
        var d = GovernanceRuleEngine.Evaluate(r, Builtins(), Settings(GovernanceMode.Enforce, "admin"));
        Assert.Equal(GovernanceDecisionKind.Allow, d.Decision);
    }

    [Fact]
    public void Specialist_rule_only_applies_to_the_specialist()
    {
        Assert.Equal(GovernanceDecisionKind.Allow, Enforce(Bash("git commit -am fix")).Decision);
        var d = Enforce(Bash("git commit -am fix", GovernanceSource.Specialist));
        Assert.Equal(GovernanceDecisionKind.Block, d.Decision);
        Assert.Contains(d.Rules, h => h.Key == "specialist-read-only");
    }

    [Fact]
    public void File_tools_are_matched_by_path()
    {
        var r = new GovernanceRequest
        {
            Source = GovernanceSource.Hook, Tool = "Read", Path = "/Users/x/.aws/credentials",
            Input = "{\"file_path\":\"/Users/x/.aws/credentials\"}", Roles = ["user"]
        };
        var d = Enforce(r);
        Assert.Equal(GovernanceDecisionKind.Block, d.Decision);
        Assert.Contains(d.Rules, h => h.Key == "protect-credentials-files");
        Assert.Equal(GovernanceDecisionKind.Allow, Enforce(r with { Path = "/Users/x/repos/a/Program.cs", Input = "Program.cs" }).Decision);
    }

    [Fact]
    public void Http_actions_are_matched_by_glob_and_warn_wins_over_monitor()
    {
        var r = new GovernanceRequest { Source = GovernanceSource.Screen, Action = "POST ExecutionPlan/card/{card}/reset", Roles = ["admin"] };
        var builtins = Builtins();
        // A regra embutida usa '*' no lugar do card; a ação gravada usa o template da rota.
        var d = GovernanceRuleEngine.Evaluate(r, builtins, Settings(GovernanceMode.Enforce));
        Assert.Equal(GovernanceDecisionKind.Warn, d.Decision);
        Assert.Equal(GovernanceOutcome.Warned, d.Outcome);
    }

    [Fact]
    public void Rule_without_any_match_dimension_never_matches()
    {
        var rule = new GovernanceRule(Guid.NewGuid(), "vazia", false);
        rule.Update("Vazia", null, null, true, GovernanceEffect.Block, 1, "x", null, new RuleMatch(), "t", Now);
        var d = GovernanceRuleEngine.Evaluate(Bash("ls"), [rule], Settings(GovernanceMode.Enforce));
        Assert.Equal(GovernanceDecisionKind.Allow, d.Decision);
    }

    [Fact]
    public void Scope_by_role_user_and_card_type()
    {
        var rule = new GovernanceRule(Guid.NewGuid(), "so-gestor", false);
        rule.Update("Só gestor", null, null, true, GovernanceEffect.Block, 1, "x",
            new RuleScope { Roles = ["gestor"], CardTypes = ["bug"] }, new RuleMatch { Tools = ["Bash"] }, "t", Now);
        var s = Settings(GovernanceMode.Enforce);
        Assert.Equal(GovernanceDecisionKind.Allow, GovernanceRuleEngine.Evaluate(Bash("ls"), [rule], s).Decision);
        var gestor = Bash("ls") with { Roles = ["Gestor"] };
        Assert.Equal(GovernanceDecisionKind.Allow, GovernanceRuleEngine.Evaluate(gestor, [rule], s).Decision);
        Assert.Equal(GovernanceDecisionKind.Block, GovernanceRuleEngine.Evaluate(gestor with { CardType = "Bug" }, [rule], s).Decision);
    }

    [Fact]
    public void Approval_blocks_with_an_explanation()
    {
        var rule = new GovernanceRule(Guid.NewGuid(), "aprovar", false);
        rule.Update("Aprovar", null, null, true, GovernanceEffect.Approval, 1, "Mover para produção", null,
            new RuleMatch { Actions = ["POST Azure/card/*/actions/test-in-production"] }, "t", Now);
        var r = new GovernanceRequest { Source = GovernanceSource.Screen, Action = "POST Azure/card/{id}/actions/test-in-production" };
        var d = GovernanceRuleEngine.Evaluate(r, [rule], Settings(GovernanceMode.Enforce));
        Assert.True(d.Blocked);
        Assert.Contains("aprovação", d.Reason);
    }

    [Fact]
    public void Invalid_regex_is_rejected_on_save()
    {
        var rule = new GovernanceRule(Guid.NewGuid(), "ruim", false);
        Assert.Throws<DomainException>(() => rule.Update("Ruim", null, null, true, GovernanceEffect.Block, 1, "x", null,
            new RuleMatch { Patterns = ["(abc"] }, "t", Now));
    }

    [Theory]
    [InlineData("mcp__prmake__*", "mcp__prmake__prmake_advance", true)]
    [InlineData("Read|Edit|Write", "Edit", true)]
    [InlineData("Read|Edit|Write", "Bash", false)]
    [InlineData("POST Azure/card/*/actions/*", "POST Azure/card/{id}/actions/ready-for-qa", true)]
    public void Glob(string glob, string value, bool expected) => Assert.Equal(expected, GovernanceRuleEngine.Glob(glob, value));

    [Theory]
    [InlineData("**/.aws/credentials", "/Users/a/.aws/credentials", true)]
    [InlineData("**/.aws/credentials", "C:\\Users\\a\\.aws\\credentials", true)]
    [InlineData("**/.prmake/db-credentials*", "/home/a/.prmake/db-credentials.json", true)]
    [InlineData("src/*.cs", "src/a/b.cs", false)]
    public void PathGlob(string glob, string value, bool expected) => Assert.Equal(expected, GovernanceRuleEngine.PathGlob(glob, value));

    [Fact]
    public void Default_milestones_are_valid()
    {
        foreach (var m in GovernanceSettings.DefaultMilestones()) m.Validate();
    }

    [Fact]
    public void Builtin_hooks_are_valid()
    {
        var order = 0;
        foreach (var h in GovernanceDefaults.Hooks) GovernanceDefaults.CreateHook(h, order += 10, Now);
    }
}
