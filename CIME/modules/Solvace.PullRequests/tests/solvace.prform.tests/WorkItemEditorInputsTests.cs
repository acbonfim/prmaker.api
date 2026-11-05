using System.Text.Json.Nodes;
using solvace.azure.application.Services;
using solvace.azure.domain.Models;
using solvace.github.application.Services;
using solvace.github.domain.Responses;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0085 r3: autocomplete de branches, estados sugeridos do card e tipo dos campos do editor de itens.</summary>
public class WorkItemEditorInputsTests
{
    private static BranchInfoResponse B(string name, bool @protected = false, bool isDefault = false) =>
        new() { Name = name, Protected = @protected, IsDefault = isDefault };

    private static readonly List<BranchInfoResponse> Branches =
    [
        B("zeta"), B("feature/abc"), B("hotfix/77434-rv"), B("hotfix/77434"), B("hotfix/177434"), B("qa"),
        B("development"), B("master", @protected: true, isDefault: true), B("release-version", @protected: true),
        B("bugfix/x-77434"), B("locked", @protected: true), B("Alpha")
    ];

    private static readonly string[] Bases = ["master", "main", "development", "qa", "release-version"];

    [Fact]
    public void Branches_order_default_then_flow_bases_and_protected_then_card_then_alphabetical()
    {
        var result = BranchSearch.Rank(Branches, null, null, Bases, "hotfix/77434", "77434").Select(b => b.Name).ToList();
        Assert.Equal(
        [
            "master", "development", "qa", "release-version", "locked",
            "hotfix/77434", "bugfix/x-77434", "hotfix/77434-rv",
            "Alpha", "feature/abc", "hotfix/177434", "zeta"
        ], result);
    }

    [Fact]
    public void Branches_filter_by_contains_ignoring_case_and_limit()
    {
        var result = BranchSearch.Rank(Branches, "HOTFIX", 2, Bases, "hotfix/77434", "77434").Select(b => b.Name).ToList();
        Assert.Equal(["hotfix/77434", "hotfix/77434-rv"], result);
    }

    [Fact]
    public void Branches_without_card_are_alphabetical_after_the_bases()
    {
        var result = BranchSearch.Rank(Branches, "fix", 50).Select(b => b.Name).ToList();
        Assert.Equal(["bugfix/x-77434", "hotfix/177434", "hotfix/77434", "hotfix/77434-rv"], result);
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(-3, 12)]
    [InlineData(1000, 12)]
    [InlineData(3, 3)]
    public void Branches_limit_is_clamped(int limit, int expected) =>
        Assert.Equal(expected, BranchSearch.Rank(Branches, null, limit).Count);

    [Fact]
    public void Flow_bases_come_from_strategy_and_suffixes_without_markers()
    {
        const string strategy = """
            {"flows": {"producao": {"legacy": {"base": "master", "prs": [{"suffix": "-dev", "target": "development"}]}},
                       "release": {"legacy": {"baseOptions": ["release-version", "hotfix-version"], "prs": [{"target": "{base}"}]}}}}
            """;
        var bases = SkillsRulesService.FlowBaseBranches(strategy, BranchRules.ParseSuffixes(null));
        Assert.Equal(["master", "main", "development", "release-version", "hotfix-version", "qa", "edge"], bases);
    }

    private static WorkItemTypeState S(string name, string category) => new() { Name = name, Category = category };

    private static readonly List<WorkItemTypeState> BugStates =
    [
        S("New", "Proposed"), S("In Development (doing)", "InProgress"), S("Ready for QA", "Resolved"),
        S("Test in production", "Resolved"), S("Closed", "Completed"), S("Removed", "Removed")
    ];

    private static readonly List<(string, string?)> Actions =
    [
        (CardStateSuggestions.DevTestInQa, "In Development (doing)"),
        (CardStateSuggestions.ReadyForQa, "ready for qa"),
        (CardStateSuggestions.TestInProduction, "Test in production")
    ];

    [Fact]
    public void States_suggest_the_configured_actions_after_the_current_one()
    {
        var options = CardStateSuggestions.Build(BugStates, "In Development (doing)", Actions);
        Assert.Equal(["Ready for QA", "Test in production"], options.Where(o => o.Suggested).Select(o => o.Name));
        Assert.Equal("ready-for-qa", options.Single(o => o.Name == "Ready for QA").Action);
        Assert.True(options.Single(o => o.Name == "In Development (doing)").Current);
        Assert.Equal("Resolved", options.Single(o => o.Name == "Ready for QA").Category);
    }

    [Fact]
    public void States_from_new_suggest_all_actions()
    {
        var options = CardStateSuggestions.Build(BugStates, "New", Actions);
        Assert.Equal(["In Development (doing)", "Ready for QA", "Test in production"], options.Where(o => o.Suggested).Select(o => o.Name));
    }

    [Fact]
    public void States_without_actions_suggest_the_next_by_order_and_category()
    {
        var us = new List<WorkItemTypeState> { S("New", "Proposed"), S("Active", "InProgress"), S("Resolved", "Resolved"), S("Closed", "Completed"), S("Removed", "Removed") };
        Assert.Equal(["Resolved"], CardStateSuggestions.Build(us, "Active", []).Where(o => o.Suggested).Select(o => o.Name));
        Assert.Equal(["Active"], CardStateSuggestions.Build(us, "New", []).Where(o => o.Suggested).Select(o => o.Name));
        Assert.DoesNotContain(CardStateSuggestions.Build(us, "Closed", []), o => o.Suggested);
    }

    [Fact]
    public void Params_without_input_get_the_default_and_admin_input_is_kept()
    {
        var kinds = JsonNode.Parse("""
            [
              {"kind": "pr", "params": [{"key": "repository"}, {"key": "source"}, {"key": "target"}, {"key": "draft"}]},
              {"kind": "comment", "params": [{"key": "target"}, {"key": "text"}]},
              {"kind": "devops-state", "params": [{"key": "state"}]},
              {"kind": "cherry-pick", "params": [{"key": "commits"}, {"key": "from", "input": "text"}]},
              {"kind": "ticket", "params": [{"key": "system", "input": "select"}, {"key": "priority"}, {"key": "title"}]}
            ]
            """)!.AsArray();
        WorkItemParamInputs.Apply(kinds, "Freshservice; Jira");

        string Input(int k, int p) => kinds[k]!["params"]![p]!["input"]!.GetValue<string>();
        Assert.Equal(["repository", "branch", "target-branch", "select"], Enumerable.Range(0, 4).Select(p => Input(0, p)));
        Assert.Equal(["select", "textarea"], Enumerable.Range(0, 2).Select(p => Input(1, p)));
        Assert.Equal("timeline", kinds[1]!["params"]![0]!["options"]![0]!.GetValue<string>());
        Assert.Equal("state", Input(2, 0));
        Assert.Equal(["commits", "text"], Enumerable.Range(0, 2).Select(p => Input(3, p)));
        Assert.Equal(["Freshservice", "Jira"], kinds[4]!["params"]![0]!["options"]!.AsArray().Select(o => o!.GetValue<string>()));
        Assert.Equal(["Baixa", "Média", "Alta"], kinds[4]!["params"]![1]!["options"]!.AsArray().Select(o => o!.GetValue<string>()));
        Assert.Equal("text", Input(4, 2));
    }

    [Fact]
    public void Ticket_system_without_configuration_is_free_text()
    {
        var kinds = JsonNode.Parse("""[{"kind": "ticket", "params": [{"key": "system", "input": "select"}]}]""")!.AsArray();
        WorkItemParamInputs.Apply(kinds, "");
        Assert.Equal("text", kinds[0]!["params"]![0]!["input"]!.GetValue<string>());
    }
}
