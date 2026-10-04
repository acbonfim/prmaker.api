namespace solvace.knowledge.domain.Entities;

public static class ArchitectureSuggestionStatus
{
    public const string Pending = "pending";
    public const string Applied = "applied";
    public const string Dismissed = "dismissed";
}

/// <summary>
/// Sugestão para a engenharia reversa (0033), proposta por uma análise (aprendizado, divergência entre a base e o
/// código) ou por uma pessoa — NUNCA grava na seção: fica na fila até um admin aplicar (pelo editor/chat) ou descartar.
/// </summary>
public class ArchitectureSuggestion
{
    public const int MaxContentLength = 20_000;

    public Guid Id { get; private set; }
    public string ProjectKey { get; private set; } = string.Empty;
    public string? SectionKey { get; private set; }
    /// <summary>learning (aprendizado da análise) | divergence (base diferente do código) | gap (lacuna: a base não
    /// cobre o assunto e precisa analisar o código — 0038) | other.</summary>
    public string Kind { get; private set; } = "other";
    /// <summary>0054: item da engenharia reversa a que a sugestão se refere (RN-012) — entra na sessão "melhorar" do documento.</summary>
    public string? ItemId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? CardNumber { get; private set; }
    public string Status { get; private set; } = ArchitectureSuggestionStatus.Pending;
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public string? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolutionNote { get; private set; }

    protected ArchitectureSuggestion() { }

    public ArchitectureSuggestion(string projectKey, string? sectionKey, string? kind, string content, string? cardNumber, string actor, DateTimeOffset now)
    {
        var text = (content ?? string.Empty).Trim();
        if (text.Length == 0) throw new DomainException("O texto da sugestão é obrigatório.");
        if (text.Length > MaxContentLength) throw new DomainException($"A sugestão pode ter no máximo {MaxContentLength} caracteres.");
        Id = Guid.NewGuid();
        ProjectKey = ArchitectureProject.NormalizeKey(projectKey);
        SectionKey = string.IsNullOrWhiteSpace(sectionKey) ? null : ArchitectureProject.NormalizeKey(sectionKey);
        // 0054: "kc" = divergência entre o Knowledge Center e o código (lista para o time de produto)
        Kind = (kind ?? "other").Trim().ToLowerInvariant() is "learning" or "divergence" or "gap" or "kc" ? kind!.Trim().ToLowerInvariant() : "other";
        Content = text;
        CardNumber = string.IsNullOrWhiteSpace(cardNumber) ? null : cardNumber.Trim();
        CreatedBy = actor;
        CreatedAt = now;
    }

    /// <summary>Liga a sugestão a um item (e ao documento da engenharia reversa) — migração e análises (0054).</summary>
    public void LinkItem(string? itemId, string? sectionKey)
    {
        if (!string.IsNullOrWhiteSpace(itemId))
        {
            var parsed = Reverse.ReverseItemKinds.ParseRef(itemId) ?? throw new DomainException($"Item inválido: '{itemId}' (ex.: RN-012).");
            ItemId = parsed.Id;
        }
        if (!string.IsNullOrWhiteSpace(sectionKey)) SectionKey = ArchitectureProject.NormalizeKey(sectionKey);
    }

    public void Resolve(string status, string? note, string actor, DateTimeOffset now)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (s is not (ArchitectureSuggestionStatus.Applied or ArchitectureSuggestionStatus.Dismissed))
            throw new DomainException("Use applied ou dismissed.");
        Status = s;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];
        ResolvedBy = actor;
        ResolvedAt = now;
    }
}
