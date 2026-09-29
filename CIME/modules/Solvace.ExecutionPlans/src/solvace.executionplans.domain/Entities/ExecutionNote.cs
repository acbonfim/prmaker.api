namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Comentário no plano de execução (0031): texto do usuário (ou do Claude) com anexos opcionais
/// (<see cref="ExecutionArtifact.NoteId"/>). O número é por card (<c>#n</c>), igual nas abas Análise e
/// Correção, para ser citado no Claude ou no PRMake ("veja o comentário 3"). Remoção lógica.
/// </summary>
public class ExecutionNote
{
    public const int MaxTextLength = 20_000;
    public const int MaxAttachments = 20;

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public int Number { get; private set; }
    public string? StepKey { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public Guid? AuthorUserId { get; private set; }
    public string AuthorName { get; private set; } = string.Empty;
    public bool FromExecutor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public string? DeletedBy { get; private set; }

    protected ExecutionNote() { }

    public ExecutionNote(Guid planId, string cardNumber, int number, string? stepKey, string? text, bool hasAttachments,
        Guid? authorUserId, string authorName, bool fromExecutor, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        PlanId = planId;
        CardNumber = cardNumber;
        Number = number;
        StepKey = string.IsNullOrWhiteSpace(stepKey) ? null : ExecutionStep.NormalizeKey(stepKey);
        Text = NormalizeText(text, hasAttachments);
        AuthorUserId = authorUserId;
        AuthorName = authorName;
        FromExecutor = fromExecutor;
        CreatedAt = now;
    }

    public bool IsDeleted => DeletedAt.HasValue;

    public void Edit(string? text, bool hasAttachments, DateTimeOffset now)
    {
        Text = NormalizeText(text, hasAttachments);
        UpdatedAt = now;
    }

    public void Delete(string actor, DateTimeOffset now)
    {
        DeletedAt = now;
        DeletedBy = actor;
    }

    /// <summary>Só o autor mexe: a mesma pessoa pela tela, ou a skill no comentário da skill (nunca no do usuário).</summary>
    public bool CanBeChangedBy(Guid? userId, bool isExecutor) =>
        FromExecutor ? isExecutor : !isExecutor && userId.HasValue && userId == AuthorUserId;

    private static string NormalizeText(string? text, bool hasAttachments)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 && !hasAttachments)
            throw new DomainException("Escreva um comentário ou anexe um arquivo.");
        if (trimmed.Length > MaxTextLength)
            throw new DomainException($"O comentário pode ter no máximo {MaxTextLength} caracteres.");
        return trimmed;
    }
}
