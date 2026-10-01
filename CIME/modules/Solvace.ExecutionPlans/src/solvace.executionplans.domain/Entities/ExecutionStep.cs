using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Uma etapa do plano de execução. Identificada pela <see cref="Key"/> (slug estável definido pela
/// skill), o que torna os envios idempotentes: reenviar a mesma etapa atualiza em vez de duplicar.
/// </summary>
public partial class ExecutionStep
{
    public const int MaxKeyLength = 80;
    public const int MaxTitleLength = 200;
    public const int MaxTextLength = 20_000;
    public const int MaxActivityLength = 500;
    public const int MaxReasonLength = 1_000;

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public int Order { get; internal set; }
    public string Title { get; private set; } = string.Empty;

    /// <summary>O que a etapa vai fazer (markdown).</summary>
    public string? Description { get; private set; }

    public string Status { get; private set; } = ExecutionStatus.Pending;

    /// <summary>Motivo do status (obrigatório em cancelada; mostrado no tooltip).</summary>
    public string? StatusReason { get; private set; }
    public string? StatusChangedBy { get; private set; }

    /// <summary>Etapa "aguardando": de quem depende — user | answer | external (0037). Null fora de "aguardando".</summary>
    public string? WaitingOn { get; private set; }

    /// <summary>O que a skill está fazendo agora nesta etapa (uma linha, atualizada ao vivo).</summary>
    public string? Activity { get; private set; }

    /// <summary>Onde parou — o que a skill precisa saber para retomar esta etapa.</summary>
    public string? Checkpoint { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Quem executa: claude | user (0024).</summary>
    public string Executor { get; private set; } = ExecutionExecutor.Claude;

    /// <summary>task | code | pr | ticket | question | validation (0024).</summary>
    public string Kind { get; private set; } = ExecutionStepKind.Task;

    /// <summary>Repositório (etapas de código/PR — uma etapa por repositório).</summary>
    public string? Repository { get; private set; }

    /// <summary>Keys das etapas que precisam terminar antes desta.</summary>
    public List<string> DependsOn { get; private set; } = [];

    protected ExecutionStep() { }

    internal ExecutionStep(Guid planId, string key, int order, string title, string? description, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        PlanId = planId;
        Key = NormalizeKey(key);
        Order = order;
        SetTitle(title);
        SetDescription(description);
        UpdatedAt = now;
    }

    public static string NormalizeKey(string? key)
    {
        var normalized = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            throw new DomainException("A chave da etapa é obrigatória.");
        if (normalized.Length > MaxKeyLength || !KeyPattern().IsMatch(normalized))
            throw new DomainException($"Chave de etapa inválida: '{key}'. Use letras minúsculas, números, '-' ou '_' (até {MaxKeyLength}).");
        return normalized;
    }

    internal void SetTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new DomainException("O título da etapa é obrigatório.");
        Title = Truncate(trimmed, MaxTitleLength);
    }

    internal void SetDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (trimmed is { Length: > MaxTextLength })
            throw new DomainException($"A descrição da etapa pode ter no máximo {MaxTextLength} caracteres.");
        Description = string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Dono, tipo, repositório e dependências (0024). Null = mantém o atual.</summary>
    internal void SetShape(string? executor, string? kind, string? repository, IEnumerable<string>? dependsOn)
    {
        if (executor is not null)
        {
            var e = executor.Trim().ToLowerInvariant();
            if (!ExecutionExecutor.All.Contains(e))
                throw new DomainException($"Executor inválido: '{executor}' (use claude ou user).");
            Executor = e;
        }
        if (kind is not null)
        {
            var k = kind.Trim().ToLowerInvariant();
            if (!ExecutionStepKind.All.Contains(k))
                throw new DomainException($"Tipo de etapa inválido: '{kind}'.");
            Kind = k;
        }
        if (repository is not null)
        {
            var r = repository.Trim();
            Repository = r.Length == 0 ? null : r.Length <= 200 ? r : r[..200];
        }
        if (dependsOn is not null)
        {
            var deps = dependsOn.Select(NormalizeKey).Distinct().ToList();
            if (deps.Contains(Key))
                throw new DomainException($"A etapa '{Key}' não pode depender dela mesma.");
            DependsOn = deps;
        }
    }

    internal void SetActivity(string? activity)
    {
        var line = activity?.Trim();
        Activity = string.IsNullOrEmpty(line) ? null : Truncate(line, MaxActivityLength);
    }

    internal void SetCheckpoint(string? checkpoint)
    {
        var trimmed = checkpoint?.Trim();
        if (trimmed is { Length: > MaxTextLength })
            throw new DomainException($"O checkpoint pode ter no máximo {MaxTextLength} caracteres.");
        Checkpoint = string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    internal void ChangeStatus(string status, string? reason, string actor, DateTimeOffset now, string? waitingOn = null)
    {
        if (!ExecutionStatus.StepStatuses.Contains(status))
            throw new DomainException($"Status de etapa inválido: '{status}'.");

        var trimmedReason = reason?.Trim();
        if (status == ExecutionStatus.Cancelled && string.IsNullOrEmpty(trimmedReason))
            throw new DomainException("Informe o motivo do cancelamento da etapa.");

        string? waiting = null;
        if (status == ExecutionStatus.Waiting)
        {
            waiting = string.IsNullOrWhiteSpace(waitingOn) ? ExecutionWaitingOn.External : waitingOn.Trim().ToLowerInvariant();
            if (!ExecutionWaitingOn.All.Contains(waiting))
                throw new DomainException($"waitingOn inválido: '{waitingOn}' (use user, answer ou external).");
            // 0037: pendência do usuário sem dizer o que ele precisa fazer não ajuda ninguém.
            if (waiting == ExecutionWaitingOn.User && string.IsNullOrEmpty(trimmedReason))
                throw new DomainException("Diga o que o usuário precisa fazer (reason) para destravar a etapa.");
        }

        if (status is ExecutionStatus.Running or ExecutionStatus.Waiting)
        {
            StartedAt ??= now;
            FinishedAt = null;
        }
        else if (ExecutionStatus.IsStepFinished(status))
        {
            FinishedAt = now;
            // Terminou: a "atividade atual" deixa de fazer sentido (o histórico fica nos logs).
            Activity = null;
        }
        else
        {
            FinishedAt = null;
        }

        Status = status;
        WaitingOn = waiting;
        StatusReason = string.IsNullOrEmpty(trimmedReason) ? null : Truncate(trimmedReason, MaxReasonLength);
        StatusChangedBy = actor;
        UpdatedAt = now;
    }

    internal void Touch(DateTimeOffset now) => UpdatedAt = now;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
    private static partial Regex KeyPattern();
}
