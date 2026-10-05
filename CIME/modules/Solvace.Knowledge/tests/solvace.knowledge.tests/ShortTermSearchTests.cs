using solvace.knowledge.application;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>0056: sigla curta ("A3", "5S") vale na busca da Base Solvace, do MCP e da engenharia reversa — só como palavra inteira.</summary>
public class ShortTermSearchTests
{
    [Fact]
    public void Short_sigla_with_digit_is_a_search_term_but_short_words_are_not()
    {
        var terms = ArchitectureSearch.Terms("Como criar um A3 no 5S de hoje?");
        Assert.Contains("a3", terms);
        Assert.Contains("5s", terms);
        Assert.DoesNotContain("um", terms);
        Assert.DoesNotContain("de", terms);
        Assert.Contains("a3", ArchitectureSearch.Terms(null, ["A3"]));
    }

    [Theory]
    [InlineData("o a3 nasce aberto", true)]
    [InlineData("a3", true)]
    [InlineData("menu melhoria > a3.", true)]
    [InlineData("sa3_registro.asp", false)]
    [InlineData("guid 4e878f2f-9a3c-4114", false)]
    [InlineData("a30 dias", false)]
    public void Short_term_matches_only_as_a_whole_word(string text, bool expected)
    {
        Assert.Equal(expected, ArchitectureSearch.HasTerm(text, "a3"));
        Assert.Equal(expected ? 1 : 0, ArchitectureSearch.CountTerm(text, "a3", out _));
    }

    [Fact]
    public void Long_terms_keep_matching_inside_words()
    {
        Assert.True(ArchitectureSearch.HasTerm("tb_sa3_a3", "sa3_"));
        Assert.Equal(2, ArchitectureSearch.CountTerm("aprovador e aprovadores", "aprovador", out var first));
        Assert.Equal(0, first);
    }
}
