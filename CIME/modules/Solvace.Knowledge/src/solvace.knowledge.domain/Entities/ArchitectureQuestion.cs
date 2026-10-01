using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Entities;

public static class ArchitectureQuestionStatus
{
    public const string Open = "open";
    public const string Answered = "answered";
    public const string Dismissed = "dismissed";
}

/// <summary>Tipo da pergunta (0040): operação/configuração, regra de negócio, técnica (implementação) ou outra.</summary>
public static class ArchitectureQuestionKind
{
    public const string Operation = "operacao";
    public const string Rule = "regra";
    public const string Technical = "tecnica";
    public const string Other = "outra";

    public static string Normalize(string? kind) => (kind ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "operacao" or "operação" or "operation" or "configuracao" or "configuração" => Operation,
        "regra" or "rule" or "regra de negocio" or "regra de negócio" => Rule,
        "tecnica" or "técnica" or "technical" => Technical,
        _ => Other
    };
}

/// <summary>
/// Pergunta feita no "Pergunte à Base Solvace" (0040). As que a base não responde (not-found/partial) formam a fila de
/// "perguntas sem resposta": o admin vê na tela e a skill base-solvace as resolve lendo o código e publicando a seção.
/// Perguntas iguais (sem acento/caixa/pontuação) somam no mesmo registro.
/// </summary>
public partial class ArchitectureQuestion
{
    public const int MaxTextLength = 600;
    public const int MaxNoteLength = 1_000;

    public Guid Id { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public string Normalized { get; private set; } = string.Empty;
    public string Kind { get; private set; } = ArchitectureQuestionKind.Other;
    /// <summary>Cobertura da última vez que foi perguntada (answered | partial | not-found | unknown).</summary>
    public string Coverage { get; private set; } = "unknown";
    public string? SuggestedProject { get; private set; }
    public string? SuggestedSection { get; private set; }
    public int Times { get; private set; }
    public DateTimeOffset FirstAskedAt { get; private set; }
    public string FirstAskedBy { get; private set; } = string.Empty;
    public DateTimeOffset LastAskedAt { get; private set; }
    public string LastAskedBy { get; private set; } = string.Empty;
    public string Status { get; private set; } = ArchitectureQuestionStatus.Open;
    public string? AnsweredProject { get; private set; }
    public string? AnsweredSection { get; private set; }
    public string? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? Note { get; private set; }

    protected ArchitectureQuestion() { }

    public ArchitectureQuestion(string text, string actor, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Text = Clean(text);
        Normalized = NormalizeText(Text);
        if (Normalized.Length == 0) throw new DomainException("A pergunta está vazia.");
        FirstAskedAt = now;
        FirstAskedBy = actor;
    }

    /// <summary>Mais uma vez perguntada: guarda a cobertura de agora. Respondida que voltou a não ter resposta reabre.</summary>
    public void Asked(string? kind, string? coverage, string? suggestedProject, string? suggestedSection, string actor, DateTimeOffset now)
    {
        Times++;
        LastAskedAt = now;
        LastAskedBy = actor;
        Kind = ArchitectureQuestionKind.Normalize(kind);
        Coverage = string.IsNullOrWhiteSpace(coverage) ? "unknown" : coverage.Trim().ToLowerInvariant();
        SuggestedProject = string.IsNullOrWhiteSpace(suggestedProject) ? null : suggestedProject.Trim();
        SuggestedSection = string.IsNullOrWhiteSpace(suggestedSection) ? null : suggestedSection.Trim();
        if (Status == ArchitectureQuestionStatus.Answered && Coverage == "not-found") Status = ArchitectureQuestionStatus.Open;
    }

    /// <summary>Sem resposta na base (fila do admin/skill).</summary>
    public bool IsGap => Coverage is "not-found" or "partial";

    public void Resolve(string status, string? projectKey, string? sectionKey, string? note, string actor, DateTimeOffset now)
    {
        var value = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (value is not (ArchitectureQuestionStatus.Answered or ArchitectureQuestionStatus.Dismissed or ArchitectureQuestionStatus.Open))
            throw new DomainException("Status inválido (answered, dismissed ou open).");
        Status = value;
        AnsweredProject = string.IsNullOrWhiteSpace(projectKey) ? null : ArchitectureProject.NormalizeKey(projectKey);
        AnsweredSection = string.IsNullOrWhiteSpace(sectionKey) ? null : ArchitectureProject.NormalizeKey(sectionKey);
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Note = cleanNote is { Length: > MaxNoteLength } ? cleanNote[..MaxNoteLength] : cleanNote;
        ResolvedBy = value == ArchitectureQuestionStatus.Open ? null : actor;
        ResolvedAt = value == ArchitectureQuestionStatus.Open ? null : now;
    }

    /// <summary>Chave de igualdade: sem acento, caixa e pontuação ("Como habilitar o módulo?" = "como habilitar o modulo").</summary>
    public static string NormalizeText(string? text)
    {
        var decomposed = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        var n = NonWord().Replace(sb.ToString(), " ").Trim();
        return n.Length <= MaxTextLength ? n : n[..MaxTextLength];
    }

    private static string Clean(string text)
    {
        var t = (text ?? string.Empty).Trim();
        return t.Length <= MaxTextLength ? t : t[..MaxTextLength];
    }

    [GeneratedRegex(@"[^a-z0-9]+")] private static partial Regex NonWord();
}
