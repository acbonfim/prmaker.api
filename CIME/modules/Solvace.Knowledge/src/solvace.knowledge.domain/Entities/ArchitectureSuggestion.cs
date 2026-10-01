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
    /// <summary>learning (aprendizado da análise) | divergence (base diferente do código) | other.</summary>
    public string Kind { get; private set; } = "other";
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
        Kind = (kind ?? "other").Trim().ToLowerInvariant() is "learning" or "divergence" ? kind!.Trim().ToLowerInvariant() : "other";
        Content = text;
        CardNumber = string.IsNullOrWhiteSpace(cardNumber) ? null : cardNumber.Trim();
        CreatedBy = actor;
        CreatedAt = now;
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
