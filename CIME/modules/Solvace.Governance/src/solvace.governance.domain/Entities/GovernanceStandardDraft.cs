namespace solvace.governance.domain.Entities;

/// <summary>
/// 0101: rascunho de importação de padrões (wiki ou engenharia reversa) — a diferença por item contra o catálogo, que o
/// admin aceita item a item ou em lote. Nada entra no catálogo sem passar por aqui.
/// </summary>
public class GovernanceStandardDraft
{
    public const string Open = "open";
    public const string Applied = "applied";

    public Guid Id { get; private set; }
    public string Kind { get; private set; } = "wiki";
    public string? Version { get; private set; }
    public string? Url { get; private set; }
    public string Status { get; private set; } = Open;
    public List<StandardDraftItem> Items { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset? AppliedAt { get; private set; }
    public string? AppliedBy { get; private set; }

    protected GovernanceStandardDraft() { }

    public GovernanceStandardDraft(Guid id, string kind, string? version, string? url, List<StandardDraftItem> items, string actor, DateTimeOffset now)
    {
        Id = id;
        Kind = kind;
        Version = version;
        Url = url;
        Items = items;
        CreatedAt = now;
        CreatedBy = actor;
    }

    public void MarkApplied(string actor, DateTimeOffset now)
    {
        if (Status != Open) throw new DomainException("Este rascunho já foi aplicado.");
        Status = Applied;
        AppliedAt = now;
        AppliedBy = actor;
    }
}

/// <summary>Um item do rascunho: added | changed | removed | same.</summary>
public sealed class StandardDraftItem
{
    public string Key { get; set; } = string.Empty;
    public string Change { get; set; } = "added";
    public StandardData? Data { get; set; }
    /// <summary>Texto anterior (changed/removed), para a tela mostrar a diferença.</summary>
    public string? PreviousTitle { get; set; }
    public string? PreviousTextPt { get; set; }
    public string? PreviousStatus { get; set; }
}
