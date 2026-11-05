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
}
