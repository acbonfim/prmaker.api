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
    public int Length { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

public class ArchitectureSectionResponse : ArchitectureSectionSummaryResponse
{
    public string Content { get; set; } = string.Empty;
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
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public List<ArchitectureSectionSummaryResponse> Sections { get; set; } = [];
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
