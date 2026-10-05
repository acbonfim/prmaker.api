using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>Feature 0058: etapa opcional de infra (AWS) — item INF com evidência aws, seção opcional e configuração.</summary>
public class ReverseInfraTests
{
    [Fact]
    public void Parser_reads_inf_item_with_aws_evidence_and_lint_requires_it()
    {
        var item = ReverseDocParser.Parse("### INF-001 — Lambda rca-fecha-a3\n- **Onde:** aws 367983645102/us-east-1 · lambda:rca-fecha-a3\nFecha o A3 às 02h.").Single();
        Assert.Equal("INF", item.Kind);
        Assert.Contains(item.Evidence, e => e.StartsWith("aws: aws 367983645102/us-east-1 · lambda:rca-fecha-a3"));
        Assert.True(ReverseItemKinds.IsKind("INF"));
        Assert.Contains("INF", ReverseDocTypes.Get("arquitetura").Kinds);

        var sem = ReverseLint.Run(ReverseDocTypes.Get("arquitetura"), "## Infraestrutura e AWS (opcional)\n### INF-001 — Lambda rca-fecha-a3\nsem evidência");
        Assert.Contains("INF-001", sem.WithoutEvidence);
        var com = ReverseLint.Run(ReverseDocTypes.Get("arquitetura"), "### INF-001 — Lambda x\n- **Onde:** aws 1/us-east-1 · lambda:x");
        Assert.DoesNotContain("INF-001", com.WithoutEvidence);
    }

    [Fact]
    public void Infra_section_is_optional_in_the_architecture_model()
    {
        var arq = ReverseDocTypes.Get("arquitetura");
        Assert.DoesNotContain(arq.Headings, h => h.Match.Contains("infra"));
        Assert.Contains("## Infraestrutura e AWS (opcional)", arq.Template);
    }

    [Fact]
    public void Default_settings_carry_the_infra_accounts_without_secrets()
    {
        Assert.Contains("367983645102", ReverseSettings.Default.Infra);
        Assert.DoesNotContain("secret", ReverseSettings.Default.Infra!, StringComparison.OrdinalIgnoreCase);
    }
}
