using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Plano de execução de uma skill (ex.: analisar-bug) sobre um card: as etapas, o status geral e
/// o sinal de vida (heartbeat) de quem executa. Toda regra de transição de status vive aqui.
/// </summary>
public class ExecutionPlan
{
    public const int MaxTitleLength = 300;
    public const int MaxSummaryLength = 20_000;

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Summary { get; private set; }

    public string Status { get; private set; } = ExecutionStatus.Pending;
    public string? StatusReason { get; private set; }
    public string? StatusChangedBy { get; private set; }

    public Guid? CreatedByUserId { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Última chamada da skill. Sem sinal por muito tempo = a sessão provavelmente caiu.</summary>
    public DateTimeOffset? LastActivityAt { get; private set; }

    /// <summary>Token de concorrência (xmin do PostgreSQL): usuário e skill mexendo ao mesmo tempo não se atropelam.</summary>
    public uint Version { get; private set; }

    public List<ExecutionStep> Steps { get; private set; } = [];

    protected ExecutionPlan() { }

    public ExecutionPlan(string cardNumber, string kind, string title, string? summary, Guid? userId, string userName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
            throw new DomainException("O número do card é obrigatório.");
        if (string.IsNullOrWhiteSpace(userName))
            throw new DomainException("O nome de quem criou o plano é obrigatório.");

        Id = Guid.NewGuid();
        CardNumber = cardNumber.Trim();
        Kind = string.IsNullOrWhiteSpace(kind) ? "analisar-bug" : kind.Trim().ToLowerInvariant();
        SetTitle(title);
        SetSummary(summary);
        CreatedByUserId = userId;
        CreatedBy = userName.Trim();
        CreatedAt = now;
        UpdatedAt = now;
        LastActivityAt = now;
    }

    /// <summary>Completed/cancelled: o plano terminou. Só "completed" pode ser reaberto (pela skill).</summary>
    public bool IsFinished => Status is ExecutionStatus.Completed or ExecutionStatus.Cancelled;

    public void SetTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new DomainException("O título do plano é obrigatório.");
        Title = trimmed.Length <= MaxTitleLength ? trimmed : trimmed[..MaxTitleLength];
    }

    private void SetSummary(string? summary)
    {
        var trimmed = summary?.Trim();
        if (trimmed is { Length: > MaxSummaryLength })
            throw new DomainException($"O resumo pode ter no máximo {MaxSummaryLength} caracteres.");
        if (!string.IsNullOrEmpty(trimmed))
            Summary = trimmed;
    }

    /// <summary>Sinal de vida da skill (toda chamada dela passa por aqui).</summary>
    public void Touch(DateTimeOffset now, bool fromExecutor)
    {
        UpdatedAt = now;
        if (fromExecutor)
            LastActivityAt = now;
    }

    /// <summary>
    /// Define/refina as etapas (upsert pela chave), na ordem enviada. Etapas que sumiram da lista:
    /// se ainda pendentes, saem do plano; se já começaram, ficam (histórico) depois das enviadas.
    /// </summary>
    public void UpsertSteps(IReadOnlyList<ExecutionStepDefinition> definitions, DateTimeOffset now)
    {
        EnsureNotCancelled();

        var seen = new HashSet<string>();
        var order = 0;
        foreach (var def in definitions)
        {
            var key = ExecutionStep.NormalizeKey(def.Key);
            if (!seen.Add(key))
                throw new DomainException($"Etapa repetida no plano: '{key}'.");

            order += 10;
            var step = FindStep(key);
            if (step is null)
            {
                Steps.Add(new ExecutionStep(Id, key, order, def.Title, def.Description, now));
            }
            else
            {
                step.Order = order;
                step.SetTitle(def.Title);
                if (def.Description is not null)
                    step.SetDescription(def.Description);
                step.Touch(now);
            }
        }

        foreach (var orphan in Steps.Where(s => !seen.Contains(s.Key)).OrderBy(s => s.Order).ToList())
        {
            if (orphan.Status == ExecutionStatus.Pending)
            {
                Steps.Remove(orphan);
            }
            else
            {
                order += 10;
                orphan.Order = order;
            }
        }

        Touch(now, fromExecutor: true);
    }

    /// <summary>
    /// Atualização de uma etapa pela skill. Etapa desconhecida é criada no fim (a skill nunca perde
    /// um envio por ter esquecido de declarar a etapa antes).
    /// </summary>
    public ExecutionStep UpdateStep(string key, string? status, string? reason, string? activity, string? checkpoint,
        string? title, string? description, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();

        var normalized = ExecutionStep.NormalizeKey(key);
        var step = FindStep(normalized);
        if (step is null)
        {
            step = new ExecutionStep(Id, normalized, NextOrder(), string.IsNullOrWhiteSpace(title) ? normalized : title, description, now);
            Steps.Add(step);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(title)) step.SetTitle(title);
            if (description is not null) step.SetDescription(description);
        }

        if (activity is not null) step.SetActivity(activity);
        if (checkpoint is not null) step.SetCheckpoint(checkpoint);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            step.ChangeStatus(normalizedStatus, reason, actor, now);

            // A skill voltou a trabalhar: o plano (pendente, com falha ou já concluído) volta a
            // "em andamento". Pausado continua pausado — só o usuário (ou a retomada) tira da pausa.
            if (normalizedStatus == ExecutionStatus.Running
                && Status is ExecutionStatus.Pending or ExecutionStatus.Failed or ExecutionStatus.Completed)
            {
                SetPlanStatus(ExecutionStatus.Running, null, actor, now);
            }
        }
        else
        {
            step.Touch(now);
        }

        Touch(now, fromExecutor: true);
        return step;
    }

    /// <summary>A atividade atual da etapa acompanha o último pedaço de andamento enviado.</summary>
    public void TrackActivity(string stepKey, string message, DateTimeOffset now)
    {
        var step = FindStep(stepKey.Trim().ToLowerInvariant());
        if (step is null || ExecutionStatus.IsStepFinished(step.Status))
            return;

        var firstLine = message.Split('\n', 2)[0].Trim().TrimStart('#', '*', '-', ' ');
        if (firstLine.Length > 0)
            step.SetActivity(firstLine);
    }

    /// <summary>O usuário cancela (pula) uma etapa que ainda não terminou.</summary>
    public ExecutionStep CancelStep(string key, string reason, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();

        var step = FindStep(ExecutionStep.NormalizeKey(key))
                   ?? throw new DomainException("Etapa não encontrada.");
        if (ExecutionStatus.IsStepFinished(step.Status))
            throw new DomainException("Esta etapa já terminou e não pode ser cancelada.");

        step.ChangeStatus(ExecutionStatus.Cancelled, reason, actor, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>
    /// Troca o status do plano (usuário: pausar/continuar/cancelar; skill: retomar/concluir/falhar).
    /// </summary>
    public void ChangeStatus(string status, string? reason, string? summary, string actor, bool fromExecutor, DateTimeOffset now)
    {
        var target = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (!ExecutionStatus.PlanStatuses.Contains(target) || target == ExecutionStatus.Pending)
            throw new DomainException($"Status de plano inválido: '{status}'.");

        if (Status == ExecutionStatus.Cancelled)
            throw new DomainException("O plano foi cancelado e não pode mudar de status.");

        switch (target)
        {
            case ExecutionStatus.Paused:
                if (Status is not (ExecutionStatus.Pending or ExecutionStatus.Running))
                    throw new DomainException("Só um plano pendente ou em andamento pode ser pausado.");
                break;

            case ExecutionStatus.Running:
                // Continuar (usuário) ou retomar (skill). Concluído só volta pela skill (reabrir).
                if (Status == ExecutionStatus.Completed && !fromExecutor)
                    throw new DomainException("O plano já foi concluído.");
                break;

            case ExecutionStatus.Cancelled:
                if (Status == ExecutionStatus.Completed)
                    throw new DomainException("O plano já foi concluído.");
                var cancelReason = string.IsNullOrWhiteSpace(reason) ? $"Plano cancelado por {actor}" : reason.Trim();
                foreach (var step in Steps.Where(s => !ExecutionStatus.IsStepFinished(s.Status)))
                    step.ChangeStatus(ExecutionStatus.Cancelled, cancelReason, actor, now);
                break;

            case ExecutionStatus.Completed:
                foreach (var step in Steps.Where(s => s.Status == ExecutionStatus.Running))
                    step.ChangeStatus(ExecutionStatus.Completed, null, actor, now);
                foreach (var step in Steps.Where(s => s.Status == ExecutionStatus.Pending))
                    step.ChangeStatus(ExecutionStatus.Cancelled, "Não executada", actor, now);
                break;

            case ExecutionStatus.Failed:
                foreach (var step in Steps.Where(s => s.Status == ExecutionStatus.Running))
                    step.ChangeStatus(ExecutionStatus.Failed, reason, actor, now);
                break;
        }

        SetSummary(summary);
        SetPlanStatus(target, reason, actor, now);
        Touch(now, fromExecutor);
    }

    private void SetPlanStatus(string status, string? reason, string actor, DateTimeOffset now)
    {
        if (status == ExecutionStatus.Running)
        {
            StartedAt ??= now;
            FinishedAt = null;
        }
        else if (status is ExecutionStatus.Completed or ExecutionStatus.Cancelled or ExecutionStatus.Failed)
        {
            FinishedAt = now;
        }

        Status = status;
        var trimmed = reason?.Trim();
        StatusReason = string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed.Length <= ExecutionStep.MaxReasonLength ? trimmed : trimmed[..ExecutionStep.MaxReasonLength];
        StatusChangedBy = actor;
    }

    private void EnsureNotCancelled()
    {
        if (Status == ExecutionStatus.Cancelled)
            throw new DomainException("O plano foi cancelado. Crie um novo plano para continuar.");
    }

    private ExecutionStep? FindStep(string key) => Steps.FirstOrDefault(s => s.Key == key);

    private int NextOrder() => (Steps.Count == 0 ? 0 : Steps.Max(s => s.Order)) + 10;
}
