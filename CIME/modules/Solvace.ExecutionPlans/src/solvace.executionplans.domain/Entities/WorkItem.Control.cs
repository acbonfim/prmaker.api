namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Card 78680: mexer numa sub-tarefa sem cancelar o item — mudar a ordem (início, fim, acima, abaixo, antes de outra),
/// pausar/continuar e cancelar só ela — respeitando as dependências (o que depende de outra nunca passa na frente dela).
/// Aqui vale para o item avulso; no item espelho o plano é a fonte (<see cref="ExecutionPlan.MoveStep"/>) e o item
/// acompanha pela sincronização. As regras de "pode/não pode" saem em <see cref="Controls"/> para a tela e o especialista.
/// </summary>
public partial class WorkItem
{
    /// <summary>Já saiu da fila (concluída, pulada, cancelada, desfeita): fica onde está e nada passa para antes dela.</summary>
    public static bool IsSettled(WorkItemTask t) => !(WorkTaskStatus.IsOpen(t.Status) || t.Status == WorkTaskStatus.Failed);

    private static string TaskName(WorkItemTask t) => $"A sub-tarefa #{t.Number}";

    /// <summary>De quem a sub-tarefa depende: as informadas/da etapa; no avulso sem nenhuma informada, as do tipo.</summary>
    public IReadOnlyList<WorkItemTask> DependenciesOf(WorkItemTask task)
    {
        if (task.DependsOn.Count > 0)
            return task.DependsOn.Select(n => Tasks.FirstOrDefault(t => t.Number == n))
                .Where(t => t is not null && !ReferenceEquals(t, task)).Select(t => t!).ToList();
        return task.StepKey is not null ? [] : WorkTaskDependencies.Implicit(Tasks, task).ToList();
    }

    /// <summary>Quem ainda não terminou e depende (direta ou indiretamente) da sub-tarefa.</summary>
    public IReadOnlyList<WorkItemTask> DependentsOf(WorkItemTask task) =>
        TaskOrdering.Dependents(OrderedTasks.ToList(), task, IsSettled, DependenciesOf);

    /// <summary>A sub-tarefa pausada que segura esta (ela mesma ou uma dependência ainda não feita), ou null.</summary>
    public WorkItemTask? HeldBlocker(WorkItemTask task) =>
        task.Held ? task : TaskOrdering.Upstream(task, IsSettled, DependenciesOf).FirstOrDefault(d => d.Held);

    /// <summary>Próxima sub-tarefa para o executor: a primeira pendente/em espera/com falha que não está pausada (nem presa a uma pausada).</summary>
    public WorkItemTask? NextTask =>
        OrderedTasks.FirstOrDefault(t => t.Status is WorkTaskStatus.Pending or WorkTaskStatus.Waiting or WorkTaskStatus.Failed
                                         && HeldBlocker(t) is null);

    /// <summary>Só sobrou sub-tarefa pausada (ou presa a uma pausada) por fazer.</summary>
    public bool OnlyHeldLeft
    {
        get
        {
            var open = Tasks.Where(t => !IsSettled(t)).ToList();
            return open.Count > 0 && open.All(t => HeldBlocker(t) is not null);
        }
    }

    /// <summary>
    /// Card 78720: das sub-tarefas que já podem começar (pendentes, sem pausa, com as dependências terminadas), todas são da
    /// pessoa — o Claude não tem o que fazer até ela agir.
    /// </summary>
    public bool OnlyPersonTurn
    {
        get
        {
            var ready = Tasks.Where(t => t.Status == WorkTaskStatus.Pending && HeldBlocker(t) is null && DependenciesOf(t).All(IsSettled)).ToList();
            return ready.Count > 0 && ready.All(t => t.Executor == ExecutionExecutor.User);
        }
    }

    /// <summary>Card 78794: alguma sub-tarefa do Claude já pode começar (pendente, sem pausa, com as dependências terminadas).</summary>
    private bool ClaudeHasReadyTask =>
        Tasks.Any(t => t.Status == WorkTaskStatus.Pending && t.Executor != ExecutionExecutor.User && HeldBlocker(t) is null
                       && DependenciesOf(t).All(IsSettled));

    private WorkItemTask FindTask(long number) =>
        Tasks.FirstOrDefault(t => t.Number == number) ?? throw new DomainException($"Sub-tarefa #{number} não é do item #{Number}.");

    private void EnsureAdjustable()
    {
        if (WorkItemStatus.IsClosed(Status) || Status == WorkItemStatus.Reverting)
            throw new DomainException($"O item #{Number} está {Status} — as sub-tarefas não mudam mais (reabra o item ou crie um novo).");
    }

    /// <summary>Sub-tarefa rodando com o executor no item: mexer nela exige parar a execução antes (quem chama interrompe).</summary>
    private void EnsureNotRunningLive(WorkItemTask task, string what)
    {
        if (task.Status == WorkTaskStatus.Running && ActiveRequestId is not null)
            throw new WorkTaskInterruptRequiredException(Number, task.Number, what);
    }

    /// <summary>A sequência nova da mudança de ordem (valida dependências); não muda nada.</summary>
    public List<WorkItemTask> ProspectMove(long taskNumber, string position, long? beforeTask)
    {
        var task = FindTask(taskNumber);
        if (IsSettled(task))
            throw new DomainException($"{TaskName(task)} já terminou ({task.Status}) — a ordem só muda entre as ainda não feitas.");
        var before = beforeTask is { } b ? FindTask(b) : null;
        var ordered = OrderedTasks.ToList();
        var sequence = TaskOrdering.Move(ordered, task, position, before, IsSettled, TaskName);
        if (TaskOrdering.Violation(sequence, IsSettled, DependenciesOf) is { } v)
            throw new DomainException(DependencyText(v.Task, v.Dependency, task));
        return sequence;
    }

    /// <summary>Sub-tarefas em andamento que a mudança de ordem atropela (a própria movida ou uma que ganhou outra na frente).</summary>
    public IReadOnlyList<WorkItemTask> RunningTouchedBy(long taskNumber, List<WorkItemTask> sequence)
    {
        var task = FindTask(taskNumber);
        var before = OrderedTasks.ToList();
        return Tasks.Where(t => t.Status == WorkTaskStatus.Running
                                && (ReferenceEquals(t, task) || sequence.IndexOf(task) < sequence.IndexOf(t) && before.IndexOf(task) > before.IndexOf(t)))
            .ToList();
    }

    private static string DependencyText(WorkItemTask task, WorkItemTask dependency, WorkItemTask moved) =>
        ReferenceEquals(task, moved)
            ? $"A #{task.Number} depende da #{dependency.Number} ({dependency.Title}) — não pode vir antes dela."
            : $"A #{task.Number} ({task.Title}) depende da #{dependency.Number} — a #{dependency.Number} não pode ir para depois dela.";

    /// <summary>
    /// Muda a ordem de uma sub-tarefa ainda não feita. Em andamento só com o executor parado (quem chama interrompe antes) —
    /// ela, ou a que ganhou outra na frente, volta a pendente; em espera/com falha, a movida volta a pendente (roda na ordem nova).
    /// </summary>
    public WorkItemTask MoveTaskTo(long taskNumber, string position, long? beforeTask, string actor, DateTimeOffset now)
    {
        if (FollowsPlan)
            throw new DomainException($"O item #{Number} é do plano — a ordem muda pela etapa do plano.");
        EnsureAdjustable();
        var task = FindTask(taskNumber);
        var sequence = ProspectMove(taskNumber, position, beforeTask);
        var touched = RunningTouchedBy(taskNumber, sequence);
        foreach (var r in touched)
            EnsureNotRunningLive(r, "mudar a ordem");
        foreach (var r in touched)
        {
            r.BackToPending(actor, ReferenceEquals(r, task) ? "Interrompida: mudou de ordem" : $"Interrompida: a #{task.Number} passou na frente", now);
            AddLog(r.Number, actor, WorkItemLogKind.Task, $"#{r.Number} {r.Title}: interrompida — volta a pendente.", now);
        }
        if (task.Status is WorkTaskStatus.Waiting or WorkTaskStatus.Failed)
        {
            task.Reopen(actor, "mudou de ordem", now);
            AddLog(task.Number, actor, WorkItemLogKind.Task, $"#{task.Number} {task.Title}: volta a pendente — mudou de ordem.", now);
        }
        if (beforeTask is { } b && position == TaskPosition.Before && FindTask(b) is { Status: WorkTaskStatus.Waiting or WorkTaskStatus.Failed } anchor)
        {
            anchor.Reopen(actor, $"a #{task.Number} passou a vir antes dela", now);
            AddLog(anchor.Number, actor, WorkItemLogKind.Task, $"#{anchor.Number} {anchor.Title}: volta a pendente — a #{task.Number} passou na frente.", now);
        }
        for (var i = 0; i < sequence.Count; i++)
            sequence[i].Order = i + 1;
        task.Touch(now);
        UpdatedAt = now;
        AddLog(task.Number, actor, WorkItemLogKind.Updated,
            $"#{task.Number} {task.Title}: {TaskPosition.Label(position, beforeTask is { } x ? $"#{x}" : null)}.", now);
        Recalculate(actor, now, failedUndo: false);
        return task;
    }

    /// <summary>
    /// Pausa (o executor não a começa, nem as que dependem dela) ou continua uma sub-tarefa ainda não feita. Em andamento só
    /// com o executor parado; em espera/com falha volta a pendente ao pausar.
    /// </summary>
    public WorkItemTask HoldTask(long taskNumber, bool hold, string? reason, string actor, DateTimeOffset now)
    {
        EnsureAdjustable();
        var task = FindTask(taskNumber);
        if (task.StepKey is not null && FollowsPlan)
            throw new DomainException($"A sub-tarefa #{taskNumber} espelha uma etapa do plano — ela pausa pela etapa.");
        if (hold)
        {
            if (IsSettled(task))
                throw new DomainException($"{TaskName(task)} já terminou ({task.Status}) — não há o que pausar.");
            if (task.Held) return task;
            EnsureNotRunningLive(task, "pausar");
            if (task.Status is WorkTaskStatus.Running or WorkTaskStatus.Waiting or WorkTaskStatus.Failed)
                task.BackToPending(actor, "Pausada", now);
        }
        else if (!task.Held)
            throw new DomainException($"{TaskName(task)} não está pausada.");
        task.SetHeld(hold, actor, now);
        var r = Clean(reason, MaxReasonLength);
        var held = hold ? DependentsOf(task) : [];
        AddLog(task.Number, actor, WorkItemLogKind.Task, hold
            ? $"#{task.Number} {task.Title}: pausada{(r is null ? string.Empty : $" — {r}")}" +
              (held.Count == 0 ? "." : $" (esperam junto: {string.Join(", ", held.Select(t => $"#{t.Number}"))}).")
            : $"#{task.Number} {task.Title}: continua{(r is null ? "." : $" — {r}")}", now);
        UpdatedAt = now;
        Recalculate(actor, now, failedUndo: false);
        return task;
    }

    /// <summary>
    /// Cancela só esta sub-tarefa (o item segue). As que dependem dela: <paramref name="cascade"/> = cancela junto; senão
    /// seguem sem ela. Em andamento só com o executor parado.
    /// </summary>
    public IReadOnlyList<WorkItemTask> CancelTask(long taskNumber, string? reason, bool cascade, string actor, DateTimeOffset now)
    {
        EnsureAdjustable();
        var task = FindTask(taskNumber);
        if (task.StepKey is not null && FollowsPlan)
            throw new DomainException($"A sub-tarefa #{taskNumber} espelha uma etapa do plano — ela cancela pela etapa.");
        if (IsSettled(task))
            throw new DomainException($"{TaskName(task)} já terminou ({task.Status}) — não dá para cancelar.");
        var targets = new List<WorkItemTask> { task };
        if (cascade) targets.AddRange(DependentsOf(task));
        foreach (var t in targets)
            EnsureNotRunningLive(t, "cancelar");
        var r = Clean(reason, MaxReasonLength);
        foreach (var t in targets)
        {
            var why = ReferenceEquals(t, task)
                ? $"Cancelada por {actor}{(r is null ? string.Empty : $": {r}")}"
                : $"Cancelada junto com a #{task.Number} (dependia dela) — {actor}";
            t.SetHeld(false, null, now);
            t.Force(WorkTaskStatus.Cancelled, why, now);
            AddLog(t.Number, actor, WorkItemLogKind.Task, $"#{t.Number} {t.Title}: cancelada — {why}.", now);
        }
        UpdatedAt = now;
        Recalculate(actor, now, failedUndo: false);
        return targets;
    }

    /// <summary>O que dá e o que não dá para fazer com cada sub-tarefa agora (e por quê) — tela e especialista.</summary>
    public WorkTaskControls Controls(WorkItemTask task)
    {
        var c = new WorkTaskControls
        {
            DependsOn = DependenciesOf(task).Select(t => t.Number).OrderBy(n => n).ToList(),
            Implicit = task.DependsOn.Count == 0 && task.StepKey is null && DependenciesOf(task).Count > 0,
            Dependents = IsSettled(task) ? [] : DependentsOf(task).Select(t => t.Number).ToList(),
            HeldBy = task.Held ? task.HeldBy ?? "uma pessoa" : null,
        };
        if (HeldBlocker(task) is { } blocker && !ReferenceEquals(blocker, task))
            c.WaitsForHeld = blocker.Number;
        var closed = WorkItemStatus.IsClosed(Status) || Status == WorkItemStatus.Reverting;
        if (closed || IsSettled(task))
        {
            var why = closed ? $"O item está {Status}." : "Já terminou.";
            c.First = c.Last = c.Up = c.Down = c.Pause = c.Cancel = why;
            c.CanResume = false;
            return c;
        }
        c.First = MoveBlock(task, TaskPosition.First);
        c.Up = MoveBlock(task, TaskPosition.Up);
        c.Down = MoveBlock(task, TaskPosition.Down);
        c.Last = MoveBlock(task, TaskPosition.Last);
        c.Pause = task.Held ? "Já está pausada." : null;
        c.CanResume = task.Held;
        c.Cancel = null;
        return c;
    }

    private string? MoveBlock(WorkItemTask task, string position)
    {
        try
        {
            var sequence = ProspectMove(task.Number, position, null);
            return sequence.SequenceEqual(OrderedTasks) ? position switch
            {
                TaskPosition.First => "Já é a primeira ainda não feita.",
                TaskPosition.Last => "Já é a última.",
                _ => "Não muda a ordem.",
            } : null;
        }
        catch (DomainException e)
        {
            return e.Message;
        }
    }

    // ---------------------------------------------------------------- item espelho: acompanha o plano

    /// <summary>Marca a mudança (a sincronização só grava o item que mudou).</summary>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    /// <summary>Pausa e dependências da etapa na sub-tarefa espelho. Devolve se mudou.</summary>
    public bool SyncStepControl(string stepKey, bool held, string? heldBy, IEnumerable<string> dependsOnKeys, DateTimeOffset now)
    {
        if (WorkItemStatus.IsClosed(Status) || Status == WorkItemStatus.Reverting) return false;
        var task = Tasks.FirstOrDefault(t => t.StepKey == stepKey);
        if (task is null) return false;
        var numbers = dependsOnKeys.Select(k => Tasks.FirstOrDefault(t => t.StepKey == k)?.Number ?? 0).Where(n => n > 0);
        var changed = task.SetDependsOn(numbers);
        changed |= task.SetHeld(held, heldBy, now);
        if (changed) UpdatedAt = now;
        return changed;
    }
}

/// <summary>Card 78680: o que dá para fazer com a sub-tarefa — null = pode; texto = por que não.</summary>
public class WorkTaskControls
{
    /// <summary>Números das sub-tarefas de que depende.</summary>
    public List<long> DependsOn { get; set; } = [];
    /// <summary>Dependência deduzida pelo tipo (PR precisa do código, chamado do script...), não informada.</summary>
    public bool Implicit { get; set; }
    /// <summary>Ainda não feitas que dependem dela (direta ou indiretamente).</summary>
    public List<long> Dependents { get; set; } = [];
    /// <summary>Pausada por quem (null = não está pausada).</summary>
    public string? HeldBy { get; set; }
    /// <summary>Espera uma dependência pausada (número dela).</summary>
    public long? WaitsForHeld { get; set; }
    public string? First { get; set; }
    public string? Up { get; set; }
    public string? Down { get; set; }
    public string? Last { get; set; }
    public string? Pause { get; set; }
    public bool CanResume { get; set; }
    public string? Cancel { get; set; }
}

/// <summary>Card 78680: a mudança atropela a sub-tarefa que o executor está fazendo — parar a execução antes (e retomar depois).</summary>
public class WorkTaskInterruptRequiredException(long item, long task, string what)
    : DomainException($"A sub-tarefa #{task} do item #{item} está sendo executada — pare a execução para {what} (ela recomeça na ordem nova).")
{
    public long ItemNumber { get; } = item;
    public long TaskNumber { get; } = task;
}
