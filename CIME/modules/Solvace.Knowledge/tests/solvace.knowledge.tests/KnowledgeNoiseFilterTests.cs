using solvace.knowledge.domain.Filtering;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>
/// Piso do filtro de dados de teste do Knowledge Center (0033) — casos reais do DEV em 2026-09-30. Se um destes
/// quebrar, a regra mudou: ajuste também o kc.py da skill base-solvace (mesma regra dos dois lados).
/// </summary>
public class KnowledgeNoiseFilterTests
{
    private static readonly string RealText = new('x', 650);

    private static KnowledgeArticleCandidate Published(int number, string title, string category = "Action Plan",
        string subcategory = "Features", string? text = null, bool categoryActive = true, bool subcategoryActive = true) =>
        new(number, title, category, subcategory, KnowledgeNoiseFilter.PublishedStatusId, false, categoryActive, subcategoryActive, text ?? RealText);

    [Theory]
    [InlineData(21, "Finding and Organizing Your Action Plans")]
    [InlineData(23, "Creating and Editing an Action Plan")]
    [InlineData(24, "Using the Checklist to Track Steps")]
    [InlineData(25, "Collaborating Through the Action Plan Feed")]
    [InlineData(26, "Checking the Action Plan History")]
    [InlineData(45, "How to set up the notification system")]
    [InlineData(46, "Modular by Design. Complete by Nature.​")]
    [InlineData(48, "One Platform, Every Stage of Maturity")]
    [InlineData(50, "Where Shopfloor Data Becomes Boardroom Decisions.")]
    [InlineData(60, "Setting up who can manage the module (Access Permissions)")]
    [InlineData(61, "Managing Kanban boards (State, Timeline, and Custom)")]
    [InlineData(62, "Viewing deadlines on the Calendar")]
    [InlineData(63, "Tracking metrics in Analytics (Report by Period)")]
    public void Real_articles_pass(int number, string title) =>
        Assert.True(KnowledgeNoiseFilter.Evaluate(Published(number, title)).Accepted);

    [Theory]
    [InlineData("title-Haroldo")]
    [InlineData("teste qa")]
    [InlineData("teste qa editado 2")]
    [InlineData("qa                                                123")]
    [InlineData("modal editado")]
    [InlineData("Teste do editor editado")]
    [InlineData("Modular by Design editado")]
    [InlineData("Probe embedded media (front validation, safe to delete)")]
    [InlineData("Teste automatizado com imagem real (Claude)")]
    [InlineData("Teste_artigo_1")]
    [InlineData("999")]
    [InlineData("123")]
    [InlineData("artigo de TESTÉ com acento")]
    public void Test_titles_are_rejected(string title) =>
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(1, title)).Accepted);

    [Theory]
    [InlineData("Test", "Subcategoria cadastro 01")]
    [InlineData("Teste QA qa", "new qa")]
    [InlineData("200 teste", "QA Test 1234")]
    [InlineData("Bell", "QA Test")]
    [InlineData("E-mail", "012345678 012345678012345678 012345678012345678 01")]
    [InlineData("categoryName-01-PUT", "x")]
    public void Test_categories_are_rejected(string category, string subcategory) =>
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(47, "Configured to Your Methodology", category, subcategory)).Accepted);

    [Fact]
    public void Draft_archived_and_deleted_never_pass_even_when_allowed()
    {
        var allowAll = new KnowledgeFilterOptions { AllowedArticles = new HashSet<int> { 21 } };
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(21, "Finding and Organizing Your Action Plans") with { StatusId = 10 }, allowAll).Accepted);
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(21, "Finding and Organizing Your Action Plans") with { StatusId = 50 }, allowAll).Accepted);
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(21, "Finding and Organizing Your Action Plans") with { IsDeleted = true }, allowAll).Accepted);
    }

    [Fact]
    public void Short_text_and_inactive_category_are_rejected()
    {
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(21, "Finding and Organizing Your Action Plans", text: "curto")).Accepted);
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(21, "Finding and Organizing Your Action Plans", categoryActive: false)).Accepted);
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(21, "Finding and Organizing Your Action Plans", subcategoryActive: false)).Accepted);
    }

    [Fact]
    public void Plugin_only_adds_rules_and_cannot_lower_the_floor()
    {
        var options = new KnowledgeFilterOptions
        {
            ExtraPatterns = ["kanban", "(invalid"],   // regex inválido é ignorado sem derrubar o piso
            ExcludedArticles = new HashSet<int> { 21 },
            MinTextLength = 10                          // abaixo do piso: não vale
        };
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(61, "Managing Kanban boards (State, Timeline, and Custom)"), options).Accepted);
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(21, "Finding and Organizing Your Action Plans"), options).Accepted);
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(23, "Creating and Editing an Action Plan", text: new string('x', 150)), options).Accepted);
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(9, "teste qa"), options).Accepted);
        Assert.True(KnowledgeNoiseFilter.Evaluate(Published(23, "Creating and Editing an Action Plan"), options).Accepted);
    }

    [Fact]
    public void Excluded_wins_over_allowed_and_allowed_skips_only_the_patterns()
    {
        var options = new KnowledgeFilterOptions
        {
            AllowedArticles = new HashSet<int> { 7, 8 },
            ExcludedArticles = new HashSet<int> { 8 }
        };
        Assert.True(KnowledgeNoiseFilter.Evaluate(Published(7, "Guia de testes do módulo"), options).Accepted);
        Assert.False(KnowledgeNoiseFilter.Evaluate(Published(8, "Guia de testes do módulo"), options).Accepted);
    }
}
