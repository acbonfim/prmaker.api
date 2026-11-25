using solvace.prform.domain.Promotion;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0074: a branch de tratamento e a base vêm do registro do PRMake, não de adivinhação no GitHub.</summary>
public class PromotionSourceTests
{
    private const string Strategy = """
    {"repositories": [{"match": "edv-solvace-apps", "kind": "revamp-frontend"}, {"match": "edv-solvace", "kind": "legacy"}],
     "flows": {
       "producao": {
         "legacy": {"base": "master", "prs": [{"suffix": "-dev", "from": "development", "target": "development"}, {"suffix": "-qa", "target": "qa"}]},
         "revamp-frontend": {"base": "trunk", "prs": [{"suffix": "-dev", "target": "development"}]}},
       "release": {
         "legacy": {"baseOptions": ["release-version", "hotfix-version"], "askBase": true, "prs": [{"suffix": "", "target": "{base}"}]}}}}
    """;

    private static readonly IReadOnlyList<string> Suffixes = PromotionSource.Suffixes(Strategy);

    [Fact]
    public void Suffixes_come_from_the_strategy_and_ignore_empty_ones()
    {
        Assert.Equal(["-dev", "-qa"], Suffixes.OrderBy(x => x).ToArray());
        Assert.Empty(PromotionSource.Suffixes(null));
        Assert.Empty(PromotionSource.Suffixes("não é json"));
    }

    [Theory]
    [InlineData("hotfix/77317-dev", "hotfix/77317")]
    [InlineData("hotfix/77317-qa", "hotfix/77317")]
    [InlineData("hotfix/77317", "hotfix/77317")]
    [InlineData("hotfix/77317-release-version", "hotfix/77317-release-version")]   // não é sufixo do PRMake
    public void The_card_branch_is_the_registered_one_without_the_prmake_suffix(string registered, string expected)
    {
        Assert.Equal(expected, PromotionSource.StripSuffix(registered, Suffixes));
    }

    [Fact]
    public void Production_flow_registers_only_suffixed_branches_and_the_principal_is_the_plain_one()
    {
        var rows = new[] { new RegisteredBranch(1, "hotfix/77317-dev", "development"), new RegisteredBranch(2, "hotfix/77317-qa", "qa") };

        var principals = PromotionSource.Principals(rows, Suffixes);

        Assert.Equal(["hotfix/77317"], principals);
        Assert.Equal("hotfix/77317", PromotionSource.ChoosePrincipal(principals, ["master", "hotfix/77317", "hotfix/77317-dev"], "hotfix/77317"));
    }

    [Fact]
    public void A_registered_principal_that_is_gone_from_the_remote_is_not_chosen()
    {
        Assert.Null(PromotionSource.ChoosePrincipal(["hotfix/77317"], ["master", "edge"], "hotfix/77317"));
    }

    [Fact]
    public void With_several_principals_the_one_named_by_the_prmake_pattern_wins()
    {
        var chosen = PromotionSource.ChoosePrincipal(["feature/x", "hotfix/77317"], ["feature/x", "hotfix/77317"], "hotfix/77317");
        Assert.Equal("hotfix/77317", chosen);
        Assert.Equal("feature/x", PromotionSource.ChoosePrincipal(["feature/x", "hotfix/77317"], ["feature/x", "hotfix/77317"], null));
    }

    [Fact]
    public void Production_flow_base_is_the_strategy_base_for_the_repository_kind()
    {
        var rows = new[] { new RegisteredBranch(1, "hotfix/77317-dev", "development") };

        Assert.Equal("master", PromotionSource.BaseOf("hotfix/77317", rows, "legacy", Strategy).Base);
        Assert.Equal("trunk", PromotionSource.BaseOf("hotfix/77317", rows, "revamp-frontend", Strategy).Base);
        Assert.Equal("master", PromotionSource.BaseOf("hotfix/77317", rows, "outro", Strategy).Base);   // sem regra: master
        Assert.Equal("master", PromotionSource.BaseOf("hotfix/77317", rows, "legacy", null).Base);
    }

    [Fact]
    public void Release_flow_base_is_where_the_prmake_sent_the_principal()
    {
        var rows = new[] { new RegisteredBranch(7, "hotfix/76462", "release-version") };

        var (baseName, reason) = PromotionSource.BaseOf("hotfix/76462", rows, "legacy", Strategy);

        Assert.Equal("release-version", baseName);
        Assert.Contains("PRMake abriu o PR", reason);
    }

    [Fact]
    public void A_later_promotion_pr_from_the_principal_does_not_change_the_base()
    {
        // o registro mais antigo (do fluxo normal) vale; PRs de promoção abertos depois da principal para edge/master não contam
        var rows = new[]
        {
            new RegisteredBranch(9, "hotfix/76462", "edge"),            // promoção, depois
            new RegisteredBranch(7, "hotfix/76462", "release-version"), // fluxo normal, antes
            new RegisteredBranch(8, "hotfix/76462", "master")
        };

        Assert.Equal("release-version", PromotionSource.BaseOf("hotfix/76462", rows, "legacy", Strategy).Base);
    }
}
