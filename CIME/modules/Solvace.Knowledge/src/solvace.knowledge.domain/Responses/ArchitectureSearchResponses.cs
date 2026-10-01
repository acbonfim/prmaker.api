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
    /// <summary>llm | human (Guia) — 0038; null em artigos.</summary>
    public string? Audience { get; set; }
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
    /// <summary>A base cobre o assunto? answered | partial | not-found | unknown (sem IA) — 0038.</summary>
    public string Coverage { get; set; } = ArchitectureCoverage.Unknown;
    /// <summary>A seção principal para ler sobre o assunto (0038).</summary>
    public ArchitectureSuggestedSection? SuggestedSection { get; set; }
}

public static class ArchitectureCoverage
{
    public const string Answered = "answered";
    public const string Partial = "partial";
    public const string NotFound = "not-found";
    public const string Unknown = "unknown";

    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "answered" or "sim" or "yes" => Answered,
        "partial" or "parcial" => Partial,
        "not-found" or "not_found" or "notfound" or "nao" or "não" or "no" => NotFound,
        _ => Unknown
    };
}

public class ArchitectureSuggestedSection
{
    public string ProjectKey { get; set; } = string.Empty;
    public string SectionKey { get; set; } = string.Empty;
    public string? Heading { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>Seção proposta pela IA (guia, aprender com um card, análise a fundo) — nada é gravado sem aceite. 0038.</summary>
public class ArchitectureSectionProposal
{
    public string ProjectKey { get; set; } = string.Empty;
    public string? SectionKey { get; set; }
    public string Audience { get; set; } = "llm";
    public string Title { get; set; } = string.Empty;
    public int? Order { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>"Analisar a fundo" uma pergunta que a base não cobre (0038).</summary>
public class ArchitectureDeepAnswerResponse
{
    public string Question { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public string Coverage { get; set; } = ArchitectureCoverage.Unknown;
    public ArchitectureSectionProposal? Proposal { get; set; }
    /// <summary>A documentação não basta — confirmar no código (vira sugestão do tipo gap).</summary>
    public bool NeedsCodeAnalysis { get; set; }
    public string? CodeHints { get; set; }
    /// <summary>projeto/seção lidos inteiros.</summary>
    public List<string> SourcesRead { get; set; } = [];
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? AiUnavailableReason { get; set; }
}

/// <summary>Guia do projeto gerado pela IA (0038) — o admin revisa e aplica.</summary>
public class ArchitectureGuideResponse
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? DisplayName { get; set; }
    public string? Tagline { get; set; }
    public string? BusinessArea { get; set; }
    public List<ArchitectureGuideSection> Sections { get; set; } = [];
    public string? Notes { get; set; }
}

public class ArchitectureGuideSection
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Content { get; set; } = string.Empty;
}

/// <summary>"Aprender com um card" (0038): o que foi lido, o que já foi sugerido e as propostas.</summary>
public class LearnFromCardResponse
{
    public string CardNumber { get; set; } = string.Empty;
    public string? CardTitle { get; set; }
    /// <summary>Resumo do que foi feito no card (IA).</summary>
    public string? Summary { get; set; }
    public List<LearnFromCardSource> Sources { get; set; } = [];
    /// <summary>Sugestões que já existem para o card (automáticas da skill ou de outra pessoa).</summary>
    public List<ArchitectureSuggestionResponse> Existing { get; set; } = [];
    public List<ArchitectureSectionProposal> Proposals { get; set; } = [];
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? AiUnavailableReason { get; set; }
}

public class LearnFromCardSource
{
    /// <summary>devops | pr | timeline | plan | suggestions.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public string? Detail { get; set; }
}
