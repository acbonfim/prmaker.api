namespace solvace.knowledge.domain.Entities;

/// <summary>Marca d'água da sincronização de cada ambiente do KC (0033): a skill só manda o que mudou depois dela.</summary>
public class KnowledgeSyncState
{
    public string Environment { get; private set; } = KnowledgeEnvironment.Dev;
    /// <summary>Maior data de alteração já recebida do KC (criação, edição ou remoção).</summary>
    public DateTimeOffset? Watermark { get; private set; }
    public DateTimeOffset? LastSyncAt { get; private set; }
    public DateTimeOffset? LastFullSyncAt { get; private set; }
    public string? LastSyncBy { get; private set; }
    public int ArticleCount { get; private set; }

    protected KnowledgeSyncState() { }

    public KnowledgeSyncState(string environment) => Environment = KnowledgeEnvironment.Normalize(environment);

    public void Register(DateTimeOffset? maxSourceChange, bool full, int articleCount, string by, DateTimeOffset now)
    {
        if (maxSourceChange is { } max && (Watermark is null || max > Watermark)) Watermark = max;
        LastSyncAt = now;
        if (full) LastFullSyncAt = now;
        LastSyncBy = by;
        ArticleCount = articleCount;
    }
}
