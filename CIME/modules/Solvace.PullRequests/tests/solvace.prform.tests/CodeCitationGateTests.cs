using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0075: o texto da análise só passa com toda citação de código fotografada (o popover existe).</summary>
public class CodeCitationGateTests
{
    // O exemplo do pedido (item 1.6), colado como texto puro.
    private const string Example = """
        Pontos suspeitos / de alteração

        solvace-asp/systems/sa3/sa3_busca_a3.asp:239-289 — ordem da inicialização.
        solvace-core/view_shared/wwwroot/js/loadSelectOptions.class.js:336-353 — restauração de data sem formato (compartilhado).
        solvace-core/view_shared/wwwroot/templates/limitlessfull/js/plugins/pickers/daterangepicker.js:325 — construtor sem isValid() (biblioteca, 36 páginas a carregam).
        """;

    private static ExecutionCodeRefHeadResponse Head(string reference, string path, int start, int end, string? repo = "edv-solvace") =>
        new() { Ref = reference, Path = path, StartLine = start, EndLine = end, Repository = repo };

    [Fact]
    public void Extracts_the_three_citations_of_the_example()
    {
        var citations = CodeCitations.Extract(Example).Select(c => c.ToString()).ToList();
        Assert.Equal([
            "solvace-asp/systems/sa3/sa3_busca_a3.asp:239-289",
            "solvace-core/view_shared/wwwroot/js/loadSelectOptions.class.js:336-353",
            "solvace-core/view_shared/wwwroot/templates/limitlessfull/js/plugins/pickers/daterangepicker.js:325"
        ], citations);
    }

    [Theory]
    [InlineData("veja `Foo.cs:12`", "Foo.cs:12")]
    [InlineData("[`a/b.ts:3-9`](ref:x)", "a/b.ts:3-9")]
    [InlineData("**src/app/x.component.html:40**", "src/app/x.component.html:40")]
    public void Extracts_inline_forms(string text, string expected) =>
        Assert.Equal(expected, Assert.Single(CodeCitations.Extract(text)).ToString());

    [Theory]
    [InlineData("https://github.com/acbonfim/x/blob/master/a/b.cs:12")]
    [InlineData("```\nsolvace-asp/a.asp:10\n```")]
    [InlineData("às 10:30 e na versão 1.2:3")]
    [InlineData("o arquivo a/b.cs sem linha")]
    public void Ignores_urls_code_blocks_and_non_citations(string text) => Assert.Empty(CodeCitations.Extract(text));

    [Fact]
    public void Ref_links_are_collected_normalized() =>
        Assert.Equal(["sa3-init", "restore-date"], CodeCitations.RefLinks("[a](ref:Sa3-Init) e [b](ref:restore-date) e [a](ref:sa3-init)"));

    [Fact]
    public void Passes_when_every_citation_has_a_snapshot()
    {
        var heads = new List<ExecutionCodeRefHeadResponse>
        {
            Head("sa3-init", "solvace-asp/systems/sa3/sa3_busca_a3.asp", 239, 289),
            Head("restore-date", "solvace-core/view_shared/wwwroot/js/loadSelectOptions.class.js", 330, 360),
            Head("drp-ctor", "solvace-core/view_shared/wwwroot/templates/limitlessfull/js/plugins/pickers/daterangepicker.js", 320, 330),
        };
        Assert.Null(CodeCitationGate.Missing(Example + "\n[x](ref:sa3-init)", heads, "75812"));
    }

    [Fact]
    public void Lists_citations_and_refs_without_snapshot()
    {
        var heads = new List<ExecutionCodeRefHeadResponse> { Head("sa3-init", "solvace-asp/systems/sa3/sa3_busca_a3.asp", 239, 289) };
        var error = CodeCitationGate.Missing(Example + "\n[y](ref:nao-existe)", heads, "75812");

        Assert.NotNull(error);
        Assert.DoesNotContain("sa3_busca_a3.asp", error);
        Assert.Contains("loadSelectOptions.class.js:336-353", error);
        Assert.Contains("daterangepicker.js:325", error);
        Assert.Contains("nao-existe", error);
        Assert.Contains("prmake-plan.sh coderef 75812", error);
    }

    [Fact]
    public void Snapshot_of_other_lines_of_the_same_file_does_not_count()
    {
        var heads = new List<ExecutionCodeRefHeadResponse> { Head("sa3-other", "solvace-asp/systems/sa3/sa3_busca_a3.asp", 500, 520) };
        Assert.NotNull(CodeCitationGate.Missing("solvace-asp/systems/sa3/sa3_busca_a3.asp:239-289", heads, "1"));
    }
}

/// <summary>0075: qual destino de PR conta como QA (Dev Test in QA automático).</summary>
public class QaBranchTests
{
    [Theory]
    [InlineData("qa", true)]
    [InlineData("QA", true)]
    [InlineData("development", false)]
    [InlineData("qa-legacy", false)]
    [InlineData(null, false)]
    public void Default_is_the_qa_branch(string? branch, bool expected) =>
        Assert.Equal(expected, solvace.executionplans.application.Contracts.ExecutionQaSettings.Default.IsQaBranch(branch));

    [Fact]
    public void Star_is_a_prefix() =>
        Assert.True(new solvace.executionplans.application.Contracts.ExecutionQaSettings(true, ["qa*", "homolog"]).IsQaBranch("qa-legacy"));
}
