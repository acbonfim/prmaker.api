namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0103: agendamento. O item avulso pode esperar um horário para começar (ou ficar em espera sem data, opcional — a pessoa
/// decide); a sub-tarefa agendada fica pausada (<see cref="WorkItemTask.Held"/>) até o horário — as que dependem dela,
/// direta ou indiretamente, esperam junto e as independentes seguem. Quem dispara no horário é o tique da fila
/// (<c>WorkItemApplication.ReleaseDueSchedulesAsync</c>), com o pedido na máquina de quem agendou.
/// </summary>
public partial class WorkItem
{
    /// <summary>Item agendado: o PRMake executa sozinho neste horário (UTC). Null = sem horário.</summary>
    public DateTimeOffset? ScheduledAt { get; private set; }
    /// <summary>Quem agendou (ou deixou em espera) — o pedido do horário vai para a máquina dela.</summary>
    public Guid? ScheduledById { get; private set; }
    public string? ScheduledBy { get; private set; }
    /// <summary>Em espera sem data: opcional, só roda quando alguém executar (ou agendar).</summary>
    public bool Parked { get; private set; }

    /// <summary>Esperando o horário ou em espera sem data — o execute comum não dispara (precisa de "executar agora").</summary>
    public bool AwaitsSchedule => ScheduledAt is not null || Parked;

    /// <summary>Próximo horário das sub-tarefas ainda não feitas que estão agendadas (null = nenhuma).</summary>
    public DateTimeOffset? NextTaskScheduleAt =>
        Tasks.Where(t => !IsSettled(t) && t.Held && t.ScheduledAt is not null).Min(t => t.ScheduledAt);

    /// <summary>Por que o item não pode ser agendado agora (null = pode).</summary>
    public string? ScheduleBlock
    {
        get
        {
            if (FollowsPlan) return "O item é do plano — agende a sub-tarefa que precisa esperar.";
            if (WorkItemStatus.IsClosed(Status) || Status is WorkItemStatus.Reverting)
                return $"O item está {Status}.";
            if (ActiveRequestId is not null || Status is WorkItemStatus.Queued or WorkItemStatus.Running)
                return "O item já está na fila/rodando — agende a sub-tarefa que precisa esperar.";
            if (Status == WorkItemStatus.Waiting)
                return "O item está aguardando uma sub-tarefa — agende a sub-tarefa.";
            return null;
        }
    }

    /// <summary>
    /// Agenda o item (<paramref name="at"/>) ou deixa em espera sem data (<paramref name="park"/>). Só item avulso parado
    /// (pendente/com falha, sem pedido na fila).
    /// </summary>
    public void Schedule(DateTimeOffset? at, bool park, string actor, Guid? actorId, DateTimeOffset now)
    {
        if (ScheduleBlock is { } why)
            throw new DomainException($"Não dá para agendar o item #{Number}: {why}");
        if (at is null && !park)
            throw new DomainException("Informe o horário (ou deixe em espera sem data).");
        ScheduledAt = at is { } a ? ScheduleTime.Validate(a, now) : null;
        Parked = ScheduledAt is null;
        ScheduledById = actorId;
        ScheduledBy = Clean(actor, 200);
        UpdatedAt = now;
        AddLog(null, actor, WorkItemLogKind.Status, ScheduledAt is { } s
            ? $"Agendado para {ScheduleTime.Format(s)} — executa sozinho nesse horário."
            : "Em espera, sem data — só roda quando alguém executar (ou agendar).", now);
    }

    /// <summary>Tira o agendamento/espera (fica pendente, roda quando alguém executar). Devolve se havia.</summary>
    public bool ClearSchedule(string actor, string? why, DateTimeOffset now)
    {
        if (!AwaitsSchedule) return false;
        ScheduledAt = null;
        Parked = false;
        ScheduledById = null;
        ScheduledBy = null;
        UpdatedAt = now;
        AddLog(null, actor, WorkItemLogKind.Status, why ?? "Agendamento removido.", now);
        return true;
    }

    /// <summary>
    /// Agenda a sub-tarefa: pausada até <paramref name="at"/> (no horário continua sozinha). Em andamento só com o executor
    /// parado (quem chama interrompe) — a não ser que o próprio executor agende a sub-tarefa que está fazendo
    /// (<paramref name="byExecutor"/>: ele segue com as independentes e encerra); em espera/com falha volta a pendente. Item
    /// espelho agenda pela etapa do plano.
    /// </summary>
    public WorkItemTask ScheduleTask(long taskNumber, DateTimeOffset at, string actor, Guid? actorId, DateTimeOffset now, bool byExecutor = false)
    {
        EnsureAdjustable();
        var task = FindTask(taskNumber);
        if (task.StepKey is not null && FollowsPlan)
            throw new DomainException($"A sub-tarefa #{taskNumber} espelha uma etapa do plano — ela agenda pela etapa.");
        if (IsSettled(task))
            throw new DomainException($"{TaskName(task)} já terminou ({task.Status}) — não há o que agendar.");
        var when = ScheduleTime.Validate(at, now);
        if (!task.Held)
        {
            if (!byExecutor) EnsureNotRunningLive(task, "agendar");
            if (task.Status is WorkTaskStatus.Running or WorkTaskStatus.Waiting or WorkTaskStatus.Failed)
                task.BackToPending(actor, $"Agendada para {ScheduleTime.Format(when)}", now);
            task.SetHeld(true, actor, now);
        }
        var changed = task.SetSchedule(when, actorId, now);
        var waiting = DependentsOf(task);
        AddLog(task.Number, actor, WorkItemLogKind.Task,
            $"#{task.Number} {task.Title}: {(changed ? "agendada" : "continua agendada")} para {ScheduleTime.Format(when)}" +
            (waiting.Count == 0 ? "." : $" (esperam junto: {string.Join(", ", waiting.Select(t => $"#{t.Number}"))}).") , now);
        UpdatedAt = now;
        Recalculate(actor, now, failedUndo: false);
        return task;
    }

    /// <summary>Carimba quem criou nas sub-tarefas que já nasceram agendadas.</summary>
    private void StampSchedules(string actor, Guid? actorId, IEnumerable<WorkItemTask> tasks, DateTimeOffset now)
    {
        foreach (var t in tasks.Where(t => t.Held && t.HeldBy is null))
        {
            t.StampSchedule(actor, actorId);
            AddLog(t.Number == 0 ? null : t.Number, actor, WorkItemLogKind.Task,
                $"{t.Title}: agendada para {ScheduleTime.Format(t.ScheduledAt!.Value)}.", now);
        }
    }

    /// <summary>Pausa/agendamento e dependências da etapa na sub-tarefa espelho. Devolve se mudou.</summary>
    private static bool SyncStepSchedule(WorkItemTask task, DateTimeOffset? scheduledAt, Guid? scheduledById, DateTimeOffset now) =>
        task.Held && task.SetSchedule(scheduledAt, scheduledById, now);
}

/// <summary>0103: item agendado/em espera — o execute comum não dispara (a tela/MCP pedem "executar agora").</summary>
public class WorkItemScheduledException(long number, DateTimeOffset? at)
    : DomainException(at is { } a
        ? $"O item #{number} está agendado para {ScheduleTime.Format(a)} — ele começa sozinho no horário. Para antecipar, peça \"executar agora\" (runNow)."
        : $"O item #{number} está em espera, sem data — só roda quando alguém decidir: \"executar agora\" (runNow) ou agendar.")
{
    public long Number { get; } = number;
    public DateTimeOffset? ScheduledAt { get; } = at;
}
