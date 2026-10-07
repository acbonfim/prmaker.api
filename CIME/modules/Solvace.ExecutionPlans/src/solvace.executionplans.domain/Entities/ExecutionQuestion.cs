using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Pergunta da skill ao usuário (0024) — respondida no PRMake ou no Claude (a skill grava a resposta dada
/// no terminal), para as duas pontas ficarem iguais. A etapa ligada fica "aguardando" até responderem.
/// </summary>
public class ExecutionQuestion
{
    public const int MaxTextLength = 20_000;
    public const int MaxAnswerLength = 20_000;
    public const int MaxOptions = 10;
    public const int MaxCancelReasonLength = 500;

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string? StepKey { get; private set; }
    public int Order { get; private set; }
    public string Text { get; private set; } = string.Empty;

    /// <summary>Opções sugeridas (jsonb): rótulo, descrição e se é a recomendada.</summary>
    public List<ExecutionQuestionOption> Options { get; private set; } = [];
    public bool AllowFreeText { get; private set; } = true;

    public string Status { get; private set; } = ExecutionQuestionStatus.Open;
    public string? Answer { get; private set; }
    public string? AnsweredBy { get; private set; }

    /// <summary>prmake (tela) | claude (terminal).</summary>
    public string? AnsweredVia { get; private set; }
    public DateTimeOffset? AnsweredAt { get; private set; }

    /// <summary>Por que foi cancelada (ex.: "Substituída pela pergunta 5") — mostrado na tela.</summary>
    public string? CancelReason { get; private set; }

    /// <summary>
    /// Número da pergunta que substituiu esta (nova rodada da análise depois de um comentário/anexo). Aberta: foi
    /// cancelada; respondida: a resposta não vale mais — vale a da pergunta nova.
    /// </summary>
    public int? ReplacedBy { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    protected ExecutionQuestion() { }

    public ExecutionQuestion(Guid planId, string? stepKey, int order, string text, IEnumerable<ExecutionQuestionOption>? options,
        bool allowFreeText, string createdBy, DateTimeOffset now)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0)
            throw new DomainException("O texto da pergunta é obrigatório.");
        if (t.Length > MaxTextLength)
            throw new DomainException($"A pergunta pode ter no máximo {MaxTextLength} caracteres.");

        var opts = (options ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o.Label))
            .Select(o => new ExecutionQuestionOption
            {
                Label = o.Label.Trim().Length <= 300 ? o.Label.Trim() : o.Label.Trim()[..300],
                Description = string.IsNullOrWhiteSpace(o.Description) ? null : o.Description.Trim(),
                Recommended = o.Recommended
            })
            .ToList();
        if (opts.Count > MaxOptions)
            throw new DomainException($"No máximo {MaxOptions} opções por pergunta.");
        if (opts.Count == 0 && !allowFreeText)
            throw new DomainException("Pergunta sem opções precisa aceitar texto livre.");

        Id = Guid.NewGuid();
        PlanId = planId;
        StepKey = string.IsNullOrWhiteSpace(stepKey) ? null : ExecutionStep.NormalizeKey(stepKey);
        Order = order;
        Text = t;
        Options = opts;
        AllowFreeText = allowFreeText;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public void AnswerWith(string answer, string actor, bool viaClaude, DateTimeOffset now)
    {
        if (Status == ExecutionQuestionStatus.Cancelled)
            throw new DomainException("Esta pergunta foi cancelada.");
        var a = (answer ?? string.Empty).Trim();
        if (a.Length == 0)
            throw new DomainException("A resposta é obrigatória.");
        if (a.Length > MaxAnswerLength)
            throw new DomainException($"A resposta pode ter no máximo {MaxAnswerLength} caracteres.");
        if (!AllowFreeText && !Options.Any(o => string.Equals(o.Label, a, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("Escolha uma das opções.");

        Answer = a;
        AnsweredBy = actor;
        AnsweredVia = viaClaude ? "claude" : "prmake";
        AnsweredAt = now;
        Status = ExecutionQuestionStatus.Answered;
    }

    public void Cancel(string? reason = null)
    {
        if (Status != ExecutionQuestionStatus.Open) return;
        Status = ExecutionQuestionStatus.Cancelled;
        var r = reason?.Trim();
        CancelReason = string.IsNullOrEmpty(r) ? null : r.Length <= MaxCancelReasonLength ? r : r[..MaxCancelReasonLength];
    }

    /// <summary>Substituída por outra pergunta: aberta é cancelada; respondida guarda a resposta, marcada como superada.</summary>
    public void ReplaceWith(int order)
    {
        if (order == Order)
            throw new DomainException("Uma pergunta não pode substituir a si mesma.");
        ReplacedBy = order;
        Cancel($"Substituída pela pergunta {order}");
    }
}
