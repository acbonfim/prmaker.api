using solvace.knowledge.domain.Filtering;
using Xunit;

namespace solvace.knowledge.tests;

public class ArchitectureProposalTests
{
    [Fact]
    public void Extracts_section_with_mermaid_inside()
    {
        var content = "Acrescentei o fluxo.\n<<<SECAO\n## Fluxo\n```mermaid\nflowchart LR\n A-->B\n```\nFim.\nSECAO>>>\nQuer mais algo?";
        var (reply, suggestion) = ArchitectureProposal.Split(content);
        Assert.Equal("Acrescentei o fluxo.\n\nQuer mais algo?", reply);
        Assert.Equal("## Fluxo\n```mermaid\nflowchart LR\n A-->B\n```\nFim.", suggestion);
    }

    [Fact]
    public void Plain_answer_has_no_suggestion()
    {
        var (reply, suggestion) = ArchitectureProposal.Split("  Falta citar o job de sincronização.  ");
        Assert.Equal("Falta citar o job de sincronização.", reply);
        Assert.Null(suggestion);
    }

    [Fact]
    public void Only_proposal_gets_default_reply()
    {
        var (reply, suggestion) = ArchitectureProposal.Split("<<<SECAO\nconteúdo\nSECAO>>>");
        Assert.Equal("Proposta de nova versão abaixo.", reply);
        Assert.Equal("conteúdo", suggestion);
    }
}
