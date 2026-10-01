namespace solvace.knowledge.domain.Responses;

/// <summary>
/// Trecho da Base Solvace que casou com a busca (0037): uma seção de projeto (<c>section</c>) ou um artigo do
/// Knowledge Center (<c>article</c>), com o título mais próximo do trecho (para a tela rolar até ele).
/// </summary>
public class ArchitectureSearchHit
{
    public string Type { get; set; } = "section";
    public string? ProjectKey { get; set; }
    public string? ProjectName { get; set; }
    public string? SectionKey { get; set; }
    public string? SectionTitle { get; set; }
    public int? ArticleNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Título markdown (## ...) mais próximo antes do trecho; null = início da seção.</summary>
    public string? Heading { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public double Score { get; set; }
    public List<string> Matched { get; set; } = [];
    /// <summary>Por que este trecho responde à pergunta (só na busca com IA).</summary>
    public string? Reason { get; set; }
}

/// <summary>Resposta da pergunta à Base Solvace com IA (0037).</summary>
public class ArchitectureAskResponse
{
    public string Question { get; set; } = string.Empty;
    /// <summary>Resposta curta da IA citando os trechos; null sem IA.</summary>
    public string? Answer { get; set; }
    public List<ArchitectureSearchHit> Results { get; set; } = [];
    /// <summary>Termos que a IA usou para procurar (sinônimos, nomes técnicos).</summary>
    public List<string> Terms { get; set; } = [];
    public bool AiUsed { get; set; }
    /// <summary>Por que a IA não foi usada (sem plugin/ApiKey, erro do provedor).</summary>
    public string? AiUnavailableReason { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
}
