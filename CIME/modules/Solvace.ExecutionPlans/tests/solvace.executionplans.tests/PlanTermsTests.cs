using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0085 r5 (1.5): User Story = plano de implementação; Bug = plano de correção.</summary>
public class PlanTermsTests
{
    [Theory]
    [InlineData("analisar-us", "implementação", "Plano de implementação")]
    [InlineData("analisar-bug", "correção", "Plano de correção")]
    [InlineData(null, "correção", "Plano de correção")]
    public void Delivery_follows_the_plan_kind(string? kind, string delivery, string plan)
    {
        Assert.Equal(delivery, PlanTerms.Delivery(kind));
        Assert.Equal(plan, PlanTerms.DeliveryPlanTitle(kind));
    }

    [Theory]
    [InlineData("Correção 77988: novo filtro", "Implementação 77988: novo filtro")]
    [InlineData("Plano de correção do card 77988 - novo filtro", "Implementação 77988: novo filtro")]
    [InlineData("Corrigir #77988: exportar", "Implementação 77988: exportar")]
    [InlineData("Novo filtro na tela", "Implementação 77988: Novo filtro na tela")]
    [InlineData("Implementação 77988: já certo", "Implementação 77988: já certo")]
    [InlineData("", "Implementação 77988")]
    public void User_story_plan_title_is_implementation(string title, string expected) =>
        Assert.Equal(expected, PlanTerms.CorrectionTitle("analisar-us", "77988", title));

    [Fact]
    public void Bug_plan_title_is_kept() =>
        Assert.Equal("Correção 77434: filtro", PlanTerms.CorrectionTitle("analisar-bug", "77434", "Correção 77434: filtro"));

    [Fact]
    public void Accepted_proposal_on_a_user_story_asks_for_the_implementation_plan()
    {
        var p = new CaseProposal("77988", 1, "Novo filtro", null, ExecutionPhase.Correction, [new() { Kind = "add", Title = "filtro" }], DateTimeOffset.UtcNow);
        var text = p.InstructionText("Ana", "analisar-us");
        Assert.Contains("plano de implementação", text);
        Assert.DoesNotContain("correção", text);
    }
}
