using solvace.knowledge.application;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Responses;
using Xunit;
using Xunit.Abstractions;

namespace solvace.knowledge.tests;

/// <summary>0071: prompt para quem usa o sistema, leitura da resposta em bloco e blocos por BM25.</summary>
public class AskPrompts0071Tests(ITestOutputHelper output)
{
    private static readonly AskSource[] Sources = [new("UC-065 — Administrador transfere usuário", "Transferir abre o diálogo."), new("RN-293 — só ativo", "Usuário ativo e não pendente.")];

    [Fact]
    public void How_to_prompt_asks_for_who_can_prerequisites_steps_and_hides_technical_names()
    {
        var prompt = ArchitectureAskPrompts.Answer("como transferir um usuário?", ArchitectureQuestionKind.Operation, Sources, 6);
        foreach (var title in new[] { "**Resumo**", "**Quem pode**", "**Antes de começar**", "**Passo a passo**", "**O que acontece depois**", "**Cuidados**", "**Não confirmado**" })
            Assert.Contains(title, prompt);
        Assert.Contains("Nada de endpoint, tabela, classe, arquivo ou código de item", prompt);
        Assert.Contains("[1] UC-065 — Administrador transfere usuário", prompt);
        Assert.Contains("[2] RN-293 — só ativo", prompt);
        Assert.Contains("<<<RESPOSTA", prompt);
    }

    [Fact]
    public void Technical_prompt_allows_technical_names_and_has_no_step_by_step_template()
    {
        var prompt = ArchitectureAskPrompts.Answer("qual endpoint transfere?", ArchitectureQuestionKind.Technical, Sources, 6);
        Assert.Contains("exceto nesta pergunta técnica", prompt);
        Assert.DoesNotContain("**Passo a passo**", prompt);
    }

    [Fact]
    public void Answer_block_is_read_with_coverage_sources_and_section()
    {
        var answer = ArchitectureAskPrompts.ParseAnswer("""
            <<<RESPOSTA
            cobertura: answered
            fontes: 2, 1, [3], x
            secao: 1
            ---
            **Resumo** — precisa ser administrador.

            1. Abra o usuário.
            RESPOSTA>>>
            """);
        Assert.NotNull(answer);
        Assert.Equal("answered", answer.Coverage);
        Assert.Equal([2, 1, 3], answer.Sources);
        Assert.Equal(1, answer.Section);
        Assert.StartsWith("**Resumo**", answer.Text);
        Assert.Contains("1. Abra o usuário.", answer.Text);
    }

    [Fact]
    public void Unclosed_block_still_gives_the_text_after_the_separator()
    {
        var answer = ArchitectureAskPrompts.ParseAnswer("<<<RESPOSTA\ncobertura: partial\nfontes: 1\nsecao: 0\n---\nSó o administrador pode.\n");
        Assert.NotNull(answer);
        Assert.Equal("partial", answer.Coverage);
        Assert.Equal("Só o administrador pode.", answer.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Resposta sem o formato pedido.")]
    [InlineData("<<<RESPOSTA\ncobertura: answered\n")]
    public void Answer_outside_the_format_is_null_so_the_caller_falls_back_to_the_search(string content) =>
        Assert.Null(ArchitectureAskPrompts.ParseAnswer(content));

    [Fact]
    public void Best_blocks_of_a_long_text_bring_the_blocks_about_the_subject_not_just_one()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "users-funcional.md"));
        var terms = ArchitectureSearch.AnswerTerms("o que preciso para transferir um usuario para outra planta?", ["transferir usuário", "unidade", "área"]);
        var blocks = ArchitectureSearch.BestBlocks(text, terms, 2400, 2);
        Assert.NotNull(blocks);
        output.WriteLine(blocks);
        Assert.True(blocks.Length <= 2400 + 20);
        Assert.Contains("ransfer", blocks);
        Assert.Contains("\n…\n", blocks);   // dois blocos
    }

    [Fact]
    public void Short_text_goes_whole_and_no_match_is_null()
    {
        Assert.Equal("curto", ArchitectureSearch.BestBlocks("curto", [("x", 1)], 100));
        Assert.Null(ArchitectureSearch.BestBlocks(new string('a', 500) + "\n## T\n" + new string('b', 500), [("zzz", 1)], 300));
    }

    [Fact]
    public void Generic_words_are_not_search_terms()
    {
        var terms = ArchitectureSearch.Terms("o que preciso para transferir um usuario para outra planta?");
        Assert.DoesNotContain("outra", terms);
        Assert.Contains("transfer", terms);
    }
}
