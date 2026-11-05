using solvace.prform.Promotion;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0085: nome de branch por destino (BranchSuffixByTarget), marcadores dos modelos e regras do card.</summary>
public class BranchRulesTests
{
    private static readonly IReadOnlyDictionary<string, string> Suffixes = BranchRules.ParseSuffixes(null);

    [Theory]
    [InlineData("release-version", "hotfix/77434-rv")]
    [InlineData("hotfix-version", "hotfix/77434-hv")]
    [InlineData("development", "hotfix/77434-dev")]
    [InlineData("qa", "hotfix/77434-qa")]
    [InlineData("edge", "hotfix/77434-edge")]
    [InlineData("release/3.2", "hotfix/77434-release-3.2")]
    public void Branch_by_target_uses_the_configured_suffix(string target, string expected) =>
        Assert.Equal(expected, BranchRules.BranchFor("hotfix/77434", target, Suffixes));

    [Fact]
    public void Template_markers_are_expanded()
    {
        var text = BranchRules.Expand("Criar {branch}{suffix:release-version} em {repository} (card {card})", "77434", "hotfix/77434", Suffixes,
            new Dictionary<string, string> { ["repository"] = "edv-solvace" });
        Assert.Equal("Criar hotfix/77434-rv em edv-solvace (card 77434)", text);
    }

    [Fact]
    public void Invalid_suffix_json_falls_back_to_the_defaults() =>
        Assert.Equal("-rv", BranchRules.SuffixFor("release-version", BranchRules.ParseSuffixes("{not json")));

    [Theory]
    [InlineData("{branch}{suffix}", "release-version", "hotfix/77434-rv")]
    [InlineData("{branch}-{target}", "release-version", "hotfix/77434-release-version")]
    [InlineData("{branch}{suffix}", "master", "hotfix/77434-master")]
    public void Promotion_branch_supports_the_suffix_marker(string pattern, string target, string expected) =>
        Assert.Equal(expected, PromotionService.BuildNewBranchName(pattern, "hotfix/77434", target, Suffixes));

    private const string FlowByArea = """[{"areaContains": "Release Management", "flow": "release"}, {"areaContains": "Product Development Team", "flow": "producao"}]""";

    private const string Strategy = """
    {"repositories": [{"match": "edv-solvace", "kind": "legacy"}],
     "flows": {
       "producao": {"legacy": {"base": "master", "prs": [{"suffix": "-dev", "from": "development", "target": "development"}, {"suffix": "-qa", "from": "qa", "target": "qa"}]}},
       "release": {"legacy": {"baseOptions": ["release-version", "hotfix-version"], "askBase": true, "prs": [{"suffix": "", "target": "{base}"}]}}
     }}
    """;

    [Fact]
    public void Flow_comes_from_the_card_area()
    {
        Assert.Equal("release", SkillsRulesService.FlowOf(@"Solvace\Release Management\Team", FlowByArea).Flow);
        Assert.Null(SkillsRulesService.FlowOf(@"Solvace\Other", FlowByArea).Flow);
    }

    [Fact]
    public void Flow_kinds_compute_pr_branches()
    {
        var kinds = SkillsRulesService.KindsOf(Strategy, "producao", "hotfix/77434");
        var legacy = Assert.Single(kinds);
        Assert.Equal(["edv-solvace"], legacy.Repositories);
        Assert.Equal(["hotfix/77434-dev", "hotfix/77434-qa"], legacy.Prs.Select(p => p.Branch));

        var release = Assert.Single(SkillsRulesService.KindsOf(Strategy, "release", "hotfix/77434"));
        Assert.True(release.AskBase);
        Assert.Equal(["release-version", "hotfix-version"], release.BaseOptions);
    }

    [Fact]
    public void Rules_markdown_names_the_branch_per_target()
    {
        var kinds = SkillsRulesService.KindsOf(Strategy, "release", "hotfix/77434");
        var rules = new CardRules("77434", "Bug", false, "Erro no filtro", @"Solvace\Release Management", "release", "Release Management",
            "hotfix/77434", "hotfix/{card}", Suffixes, kinds, "edv-solvace", "legacy", "AB#{card} {summary}", "AB#{card} {TARGET}",
            "{branch}{suffix}", new Dictionary<string, string>(), null);
        var md = SkillsRulesService.Markdown(rules);
        Assert.Contains("`hotfix/77434-rv`", md);
        Assert.Contains("Nunca mesclar PR", md);
        Assert.Equal("release-version/hotfix-version", rules.FlowTargets());
        Assert.Contains("hotfix/77434-rv", SkillsRulesService.SummaryMarkdown(rules));
    }

    // ── Rodada 6: User Story é feature/, e as branches seguem o repositório ───────────────────────

    [Theory]
    [InlineData(false, "hotfix/{card}", "feature/{card}", "hotfix/77988")]
    [InlineData(true, "hotfix/{card}", "feature/{card}", "feature/77988")]
    [InlineData(true, "hotfix/{card}", null, "feature/77988")]       // sem a chave da US: feature/, nunca hotfix/
    [InlineData(true, "hotfix/{card}", "us/{card}", "us/77988")]     // o admin pode trocar
    [InlineData(false, null, "feature/{card}", "hotfix/77988")]
    public void Main_branch_follows_the_card_type(bool userStory, string? bug, string? us, string expected) =>
        Assert.Equal(expected, BranchRules.MainBranch(BranchRules.PatternFor(userStory, bug, us), "77988"));

    private const string RepoStrategy = """
    {"repositories": [{"match": "edv-solvace-apps", "kind": "revamp-frontend"}, {"match": "edv-solvace", "kind": "legacy"}, {"match": "revamp-*", "kind": "revamp-backend"}],
     "flows": {
       "producao": {
         "legacy": {"base": "master", "prs": [{"suffix": "-dev", "from": "development", "target": "development"}, {"suffix": "-qa", "from": "qa", "target": "qa"}]},
         "revamp-backend": {"base": "master", "prs": [{"suffix": "-dev", "from": "development", "target": "development"}]},
         "revamp-frontend": {"base": "master", "prs": [{"suffix": "-dev", "from": "development", "target": "development"}, {"suffix": "-qa", "from": "qa", "target": "qa"}]}},
       "release": {
         "revamp-backend": {"baseOptions": ["release-version", "hotfix-version"], "askBase": true, "prs": [{"suffix": "", "target": "{base}"}]}}
     }}
    """;

    private static CardRules RulesFor(string flow, bool userStory = false)
    {
        var main = userStory ? "feature/77988" : "hotfix/77988";
        return new CardRules("77988", userStory ? "User Story" : "Bug", userStory, null, null, flow, null, main, "x", Suffixes,
            SkillsRulesService.KindsOf(RepoStrategy, flow, main), "edv-solvace", "legacy", "", "", "",
            new Dictionary<string, string> { [SkillsConfigurationKeys.BranchStrategy] = RepoStrategy }, null);
    }

    [Fact]
    public void Revamp_backend_has_no_qa_branch_but_legacy_and_frontend_do()
    {
        var backend = SkillsRulesService.PlanForRepository(RulesFor("producao"), "revamp-Users");
        Assert.True(backend.Known);
        Assert.Equal("revamp-backend", backend.Kind);
        Assert.Equal(["development"], backend.Targets.Select(t => t.Target));
        Assert.Equal("hotfix/77988-dev", backend.Targets[0].Branch);

        Assert.Equal(["development", "qa"], SkillsRulesService.PlanForRepository(RulesFor("producao"), "edv-solvace").Targets.Select(t => t.Target));
        Assert.Equal(["development", "qa"], SkillsRulesService.PlanForRepository(RulesFor("producao"), "solvace/edv-solvace-apps").Targets.Select(t => t.Target));
    }

    [Fact]
    public void Repository_plan_uses_the_user_story_branch_and_expands_the_release_bases()
    {
        var plan = SkillsRulesService.PlanForRepository(RulesFor("release", userStory: true), "revamp-Users");
        Assert.Equal(["release-version", "hotfix-version"], plan.Targets.Select(t => t.Target));
        Assert.Equal(["feature/77988-rv", "feature/77988-hv"], plan.Targets.Select(t => t.Branch));
        Assert.Equal("release-version", plan.Base);
    }

    [Fact]
    public void Repository_plan_is_unknown_without_a_rule_or_without_the_kind_in_the_flow()
    {
        Assert.False(SkillsRulesService.PlanForRepository(RulesFor("producao"), "outro-repo").Known);
        Assert.False(SkillsRulesService.PlanForRepository(RulesFor("release"), "edv-solvace").Known); // release sem legacy neste fluxo
        Assert.False(SkillsRulesService.PlanForRepository(RulesFor("producao"), null).Known);
        Assert.Empty(SkillsRulesService.PlanForRepository(RulesFor("producao"), "outro-repo").Targets);
    }
}
