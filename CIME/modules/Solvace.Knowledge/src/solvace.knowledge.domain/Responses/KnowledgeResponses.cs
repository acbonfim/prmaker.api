namespace solvace.knowledge.domain.Responses;

public class KnowledgeSyncResponse
{
    public string Environment { get; set; } = string.Empty;
    public int Received { get; set; }
    public int Accepted { get; set; }
    public int Changed { get; set; }
    public int Rejected { get; set; }
    public int Removed { get; set; }
    public int Total { get; set; }
    public DateTimeOffset? Watermark { get; set; }
    /// <summary>Motivo → quantidade (ex.: "título parece teste" → 12).</summary>
    public Dictionary<string, int> RejectedReasons { get; set; } = new();
}

public class KnowledgeStateResponse
{
    /// <summary>Ambiente ativo no plugin "Knowledge Center Configurations".</summary>
    public string ActiveEnvironment { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public DateTimeOffset? Watermark { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset? LastFullSyncAt { get; set; }
    public string? LastSyncBy { get; set; }
    public int ArticleCount { get; set; }
    /// <summary>As regras do filtro mudaram desde a última carga completa — a skill faz carga completa.</summary>
    public bool FilterChanged { get; set; }
}

public class KnowledgeArticleResponse
{
    public int ArticleNumber { get; set; }
    public string Reference => $"ART-{ArticleNumber}";
    public string Environment { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    public List<string> Tags { get; set; } = [];
    /// <summary>Texto inteiro (detalhe) ou trecho (busca).</summary>
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset? SourceUpdatedAt { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}

public class ArchitectureSectionSummaryResponse
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public int Version { get; set; }
    public string Source { get; set; } = string.Empty;
    /// <summary>llm | human (0038).</summary>
    public string Audience { get; set; } = "llm";
    public int Length { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

public class ArchitectureSectionResponse : ArchitectureSectionSummaryResponse
{
    public string Content { get; set; } = string.Empty;
}

/// <summary>0070: sumário de uma seção grande — a tela busca cada pedaço sob demanda (<c>parts</c>).</summary>
public class ArchitectureSectionOutlineResponse : ArchitectureSectionSummaryResponse
{
    /// <summary>Marca do conteúdo (muda quando a seção muda) — vai nos pedidos de pedaços (<c>?v=</c>).</summary>
    public string Hash { get; set; } = string.Empty;
    public List<ArchitectureSectionChunkResponse> Chunks { get; set; } = [];
}

public class ArchitectureSectionChunkResponse
{
    public int Index { get; set; }
    public int Length { get; set; }
    /// <summary>IDs dos itens (RN-012, UC-003...) que começam neste pedaço.</summary>
    public List<string> Ids { get; set; } = [];
    /// <summary>Altura estimada (px) para o espaço reservado.</summary>
    public int Estimate { get; set; }
}

public class ArchitectureSectionPartResponse
{
    public int Index { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class ArchitectureSectionPartsResponse
{
    public string Hash { get; set; } = string.Empty;
    public List<ArchitectureSectionPartResponse> Parts { get; set; } = [];
}

public class ArchitectureProjectResponse
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Repository { get; set; }
    public string? Summary { get; set; }
    public List<string> Keywords { get; set; } = [];
    public string? SourceCommit { get; set; }
    public string? SourceBranch { get; set; }
    public DateTimeOffset? SourceMappedAt { get; set; }
    public int Order { get; set; }
    /// <summary>0038: nome, frase e área para pessoas (fora do espelho das skills).</summary>
    public string? DisplayName { get; set; }
    public string? Tagline { get; set; }
    public string? BusinessArea { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public List<ArchitectureSectionSummaryResponse> Sections { get; set; } = [];
    /// <summary>Do que este projeto depende (0034).</summary>
    public List<solvace.knowledge.domain.Entities.ArchitectureRelation> Relations { get; set; } = [];
    /// <summary>Quem depende deste projeto (calculado das relações dos outros).</summary>
    public List<ArchitectureIncomingRelation> UsedBy { get; set; } = [];
    /// <summary>0054: seções antigas substituídas pela engenharia reversa (chave → por quais documentos) — histórico na tela.</summary>
    public Dictionary<string, string> SupersededSections { get; set; } = [];

}

public class ArchitectureIncomingRelation
{
    public string Source { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? Evidence { get; set; }
}

/// <summary>Grafo do ecossistema (0034): projetos, serviços externos e as dependências entre eles.</summary>
public class ArchitectureGraphResponse
{
    public List<ArchitectureGraphNode> Nodes { get; set; } = [];
    public List<ArchitectureGraphEdge> Edges { get; set; } = [];
}

public class ArchitectureGraphNode
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Tipo do projeto ou "external".</summary>
    public string Kind { get; set; } = string.Empty;
    public bool Mapped { get; set; }
}

public class ArchitectureGraphEdge
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    /// <summary>Quantas relações desse tipo entre os dois (agrupadas numa aresta).</summary>
    public int Count { get; set; }
    public List<string> Details { get; set; } = [];
    /// <summary>0066: de onde a ligação vem — <c>base</c> (extrator/curadoria), <c>engenharia</c> (itens INT publicados) ou <c>ambos</c>.</summary>
    public string Origin { get; set; } = "base";
    /// <summary>0066: os itens <c>INT</c> da engenharia reversa por trás da ligação (abrem o item na tela).</summary>
    public List<ArchitectureGraphEdgeItem> Items { get; set; } = [];
}

/// <summary>Item da engenharia reversa que sustenta uma ligação do mapa (0066).</summary>
public class ArchitectureGraphEdgeItem
{
    /// <summary><c>modulo#INT-001</c>.</summary>
    public string Ref { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Mechanism { get; set; }
    public string? Contract { get; set; }
    public bool ToConfirm { get; set; }
}

public class ArchitectureSectionVersionResponse
{
    public int Version { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Só no detalhe de uma versão.</summary>
    public string? Content { get; set; }
}

/// <summary>Resumo do pacote do espelho local: a skill só baixa o .zip quando o hash muda.</summary>
public class ArchitectureExportManifest
{
    public string Hash { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public int Projects { get; set; }
    public int Sections { get; set; }
    public string KnowledgeEnvironment { get; set; } = string.Empty;
    public int KnowledgeArticles { get; set; }
}

/// <summary>Pergunta do "Pergunte" (0040) — fila de perguntas sem resposta.</summary>
public class ArchitectureQuestionResponse
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty;
    public string? SuggestedProject { get; set; }
    public string? SuggestedSection { get; set; }
    public int Times { get; set; }
    public DateTimeOffset FirstAskedAt { get; set; }
    public string FirstAskedBy { get; set; } = string.Empty;
    public DateTimeOffset LastAskedAt { get; set; }
    public string LastAskedBy { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AnsweredProject { get; set; }
    public string? AnsweredSection { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? Note { get; set; }
}

public class ArchitectureSuggestionResponse
{
    public Guid Id { get; set; }
    /// <summary>0054: item da engenharia reversa (RN-012).</summary>
    public string? ItemId { get; set; }
    public string ProjectKey { get; set; } = string.Empty;
    public string? SectionKey { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? CardNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
}
