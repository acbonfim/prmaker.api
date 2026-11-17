using solvace.azure.application.Services;
using solvace.azure.domain.Options;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// Card 78525: a especificação da US (refinamento) chega como um documento com uma linha de título por seção
/// ("1. High Level Business Specification (PM)"...) e cada seção vai para o seu campo do card.
/// </summary>
public class UserStorySpecTests
{
    private static readonly IReadOnlyList<(string Section, string Field)> Map =
        UserStorySpec.ParseMap(null, AzureDevOpsFieldKeys.DefaultUserStorySpecFieldsJson);

    private const string Document = """
        1. High Level Business Specification (PM)
        Problema. Usuários da Nova Water precisam digitar usuário e senha toda vez.
        Fora do escopo. SSO corporativo.
        2. Detailed Business Specification (PO)
        Regras de negócio propostas

        1. A caixa "Manter conectado" aparece no login somente se o ambiente tiver a opção habilitada.
        3. Specification Main Highlights

        * Pedido da Nova Water (card 78505).
        4. Technical Specification (Tech Lead / Architect)
        Estado atual no código (`edv-solvace-apps`).
        5. Main Validation Scenarios (QA)
        """;

    [Fact]
    public void Default_map_follows_the_form_order()
    {
        Assert.Equal(["System.Description", "Custom.DetailedBusinessSpecification", "Custom.SpecificationMainHighlights",
            "Custom.TechnicalSpecification", "Microsoft.VSTS.Common.AcceptanceCriteria"], Map.Select(m => m.Field));
    }

    [Fact]
    public void Document_is_split_by_the_section_titles_and_empty_sections_are_left_out()
    {
        var parts = UserStorySpec.Split(Document, Map);

        Assert.Equal(4, parts.Count);
        Assert.StartsWith("Problema. Usuários", parts["High Level Business Specification (PM)"]);
        // "1. A caixa..." dentro da seção não é título de seção.
        Assert.Contains("1. A caixa \"Manter conectado\"", parts["Detailed Business Specification (PO)"]);
        Assert.Contains("Pedido da Nova Water", parts["Specification Main Highlights"]);
        Assert.Contains("edv-solvace-apps", parts["Technical Specification (Tech Lead / Architect)"]);
        Assert.False(parts.ContainsKey("Main Validation Scenarios (QA)"));
    }

    [Theory]
    [InlineData("## Technical Specification", "Technical Specification (Tech Lead / Architect)")]
    [InlineData("**5. Main Validation Scenarios (QA):**", "Main Validation Scenarios (QA)")]
    [InlineData("detailed business specification", "Detailed Business Specification (PO)")]
    [InlineData("Regras de negócio propostas", null)]
    public void Titles_match_with_numbering_markup_and_without_the_parentheses(string line, string? section) =>
        Assert.Equal(section, UserStorySpec.MatchSection(line, Map));

    [Fact]
    public void Markdown_becomes_html_with_lists_and_tables()
    {
        var html = UserStorySpec.ToHtml("**Escopo.** caixa\n\n* um\n* dois\n\n| A | B |\n|---|---|\n| 1 | 2 |");
        Assert.Contains("<strong>Escopo.</strong>", html);
        Assert.Contains("<li>um</li>", html);
        Assert.Contains("<table>", html);
    }

    [Fact]
    public void Invalid_configuration_falls_back_to_the_default() =>
        Assert.Equal(5, UserStorySpec.ParseMap("não é json", AzureDevOpsFieldKeys.DefaultUserStorySpecFieldsJson).Count);
}
