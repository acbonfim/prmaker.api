using solvace.prform.domain.Promotion;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0074: perfis de destino, tipo do repositório e resolução de {version} contra as branches do remoto.</summary>
public class PromotionTargetTests
{
    private const string Profiles = """
    {"profiles": {
      "legacy": {"label": "Netcore (legado)", "targets": [{"branch": "hotfix-{version}"}]},
      "integration-api": {"targets": [{"branch": "release-{version}"}, {"branch": "master"}]},
      "revamp-frontend": {"label": "Angular", "targets": [
        {"branch": "release-{version}"}, {"branch": "edge"}, {"branch": "master", "draft": true, "mode": "direct"}]}
    }}
    """;

    private const string Strategy = """
    {"repositories": [
      {"match": "edv-solvace-apps", "kind": "revamp-frontend"},
      {"match": "edv-solvace-api", "kind": "integration-api"},
      {"match": "edv-solvace", "kind": "legacy"},
      {"match": "revamp-*", "kind": "revamp-backend"}]}
    """;

    [Fact]
    public void Profiles_are_parsed_with_draft_and_mode()
    {
        var set = PromotionProfileSet.Parse(Profiles);

        var angular = set.Find("revamp-frontend")!;
        Assert.Equal("Angular", angular.Label);
        Assert.Equal(3, angular.Targets.Count);
        Assert.False(angular.Targets[0].Draft);
        Assert.Equal(PromotionMode.CherryPick, angular.Targets[0].Mode);
        Assert.True(angular.Targets[2].Draft);
        Assert.Equal(PromotionMode.Direct, angular.Targets[2].Mode);
        Assert.Equal("integration-api", set.Find("integration-api")!.Label); // sem label = a chave
        Assert.Null(set.Find("outro"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{\"profiles\": 3}")]
    public void Invalid_or_missing_config_gives_an_empty_set(string? json)
    {
        Assert.Empty(PromotionProfileSet.Parse(json).All);
    }

    [Theory]
    [InlineData("edv-solvace", "legacy")]
    [InlineData("edv-solvace-apps", "revamp-frontend")]
    [InlineData("edv-solvace-api", "integration-api")]
    [InlineData("Solvace-Org/EDV-Solvace-Apps", "revamp-frontend")]
    [InlineData("revamp-vacations", "revamp-backend")]
    public void Repository_kind_follows_the_first_matching_rule(string repo, string kind)
    {
        Assert.Equal(kind, BranchStrategyKinds.KindOf(repo, Strategy));
    }

    [Fact]
    public void Repository_without_rule_has_no_kind()
    {
        Assert.Null(BranchStrategyKinds.KindOf("outro-repo", Strategy));
        Assert.Null(BranchStrategyKinds.KindOf("edv-solvace", null));
    }

    [Theory]
    [InlineData("V3.274.10", "3.274.10")]
    [InlineData("v3.274.10", "3.274.10")]
    [InlineData(" 3.274.10 ", "3.274.10")]
    [InlineData("3.274", "3.274")]
    [InlineData("3.274.10-rc1", "3.274.10-rc1")]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("3", null)]
    public void Version_is_normalized(string? raw, string? expected)
    {
        Assert.Equal(expected, PromotionTargetResolver.NormalizeVersion(raw));
    }

    private static readonly string[] Remote = ["master", "edge", "development", "release-3.274.10", "hotfix-3.274.10", "release-3.273.0"];

    [Fact]
    public void Template_with_version_resolves_to_the_literal_branch()
    {
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("release-{version}"), "V3.274.10", Remote);
        Assert.Equal(PromotionTargetStatus.Resolved, r.Status);
        Assert.Equal("release-3.274.10", r.Branch);
    }

    [Fact]
    public void Literal_target_is_checked_for_existence()
    {
        Assert.Equal("master", PromotionTargetResolver.Resolve(new PromotionTarget("master", true), "3.274.10", Remote).Branch);
        var missing = PromotionTargetResolver.Resolve(new PromotionTarget("qa"), "3.274.10", Remote);
        Assert.Equal(PromotionTargetStatus.Missing, missing.Status);
        Assert.Null(missing.Branch);
    }

    [Fact]
    public void Draft_and_mode_are_carried_over()
    {
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("master", true, PromotionMode.Direct), "3.274.10", Remote);
        Assert.True(r.Draft);
        Assert.Equal(PromotionMode.Direct, r.Mode);
    }

    [Theory]
    [InlineData("release/3.274.10")]
    [InlineData("release_3.274.10")]
    [InlineData("release-V3.274.10")]
    [InlineData("Release-3.274.10")]
    public void Separator_and_v_variations_are_found_when_the_literal_is_absent(string actual)
    {
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("release-{version}"), "3.274.10", ["master", actual]);
        Assert.Equal(PromotionTargetStatus.Resolved, r.Status);
        Assert.Equal(actual, r.Branch);
    }

    [Fact]
    public void Several_candidates_are_ambiguous_and_listed()
    {
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("release-{version}"), "3.274.10", ["release/3.274.10", "release_3.274.10"]);
        Assert.Equal(PromotionTargetStatus.Ambiguous, r.Status);
        Assert.Null(r.Branch);
        Assert.Equal(2, r.Candidates.Count);
    }

    [Fact]
    public void The_literal_wins_over_variations()
    {
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("release-{version}"), "3.274.10", ["release-3.274.10", "release/3.274.10"]);
        Assert.Equal(PromotionTargetStatus.Resolved, r.Status);
        Assert.Equal("release-3.274.10", r.Branch);
    }

    [Fact]
    public void A_prefix_of_the_version_does_not_match()
    {
        // release-3.274.1 não é release-3.274.10, e 3.274.100 também não.
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("release-{version}"), "3.274.10", ["release-3.274.1", "release-3.274.100"]);
        Assert.Equal(PromotionTargetStatus.Missing, r.Status);
    }

    [Fact]
    public void Invalid_version_with_a_versioned_template_is_missing()
    {
        Assert.Equal(PromotionTargetStatus.Missing, PromotionTargetResolver.Resolve(new PromotionTarget("hotfix-{version}"), "???", Remote).Status);
    }

    // ── nomes literais (release-version / hotfix-version são branches reais) ──────────────────────

    private static readonly string[] RealRemote = ["master", "edge", "development", "release-version", "hotfix-version", "release-3.274.9"];

    [Fact]
    public void Literal_branch_names_win_and_the_version_is_not_substituted()
    {
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("hotfix-version"), "3.274.10", RealRemote);

        Assert.Equal(PromotionTargetStatus.Resolved, r.Status);
        Assert.Equal("hotfix-version", r.Branch);
    }

    [Fact]
    public void A_name_ending_in_version_falls_back_to_the_numbered_branch_when_the_literal_does_not_exist()
    {
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("release-version"), "3.274.10", ["master", "release-3.274.10"]);

        Assert.Equal(PromotionTargetStatus.Resolved, r.Status);
        Assert.Equal("release-3.274.10", r.Branch);
    }

    [Fact]
    public void Missing_literal_shows_the_name_as_written_without_braces()
    {
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("release-version"), "3.274.10", ["master"]);

        Assert.Equal(PromotionTargetStatus.Missing, r.Status);
        Assert.Equal("release-version", r.Expected);
    }

    [Fact]
    public void Missing_template_shows_the_expected_name_with_the_version_in_place()
    {
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("release-{version}"), "V3.274.10", ["master"]);

        Assert.Equal(PromotionTargetStatus.Missing, r.Status);
        Assert.Equal("release-3.274.10", r.Expected);
        Assert.DoesNotContain("{", r.Expected);
    }

    [Fact]
    public void A_literal_that_is_not_about_the_version_never_gets_a_numbered_fallback()
    {
        var r = PromotionTargetResolver.Resolve(new PromotionTarget("edge"), "3.274.10", ["master", "edge-3.274.10"]);

        Assert.Equal(PromotionTargetStatus.Missing, r.Status);
    }
}
