using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0085: item de trabalho do card — uma entrega (PR para release-version, ajuste pós-QA...) com sub-tarefas ordenadas.
/// O <see cref="Number"/> vem da sequência global <c>execution.work_item_number_seq</c> (compartilhada com as
/// sub-tarefas): o número identifica item ou sub-tarefa sem ambiguidade. Só edita pendente (item e sub-tarefas).
/// </summary>
public class WorkItem
{
    public const int MaxTitleLength = 300;
    public const int MaxDescriptionLength = 50_000;
    public const int MaxTasks = 50;
    public const int MaxReasonLength = 1_000;
    public const int MaxTemplateKeyLength = 80;
    public const int MaxLinks = 50;
    public const int MaxLinkUrlLength = 1_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    /// <summary>Número da sequência global (gerado pelo banco no insert).</summary>
    public long Number { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public string Type { get; private set; } = WorkItemType.Bug;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Status { get; private set; } = WorkItemStatus.Pending;
    public string Origin { get; private set; } = WorkItemOrigin.User;
    public string? TemplateKey { get; private set; }
    public Guid? PlanId { get; private set; }
    /// <summary>sha256 (hex) do tipo + tipos das sub-tarefas + parâmetros-chave — evita item repetido no card.</summary>
    public string Fingerprint { get; private set; } = string.Empty;
    public Guid? CreatedBy { get; private set; }
    public string CreatedByName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CancelReason { get; private set; }
    /// <summary>Pedido de execução ativo do item (work-item/work-item-undo) — null fora da fila.</summary>
    public Guid? ActiveRequestId { get; private set; }
    /// <summary>JSON (jsonb) de <see cref="WorkItemLink"/>.</summary>
    public string LinksJson { get; private set; } = "[]";
    /// <summary>xmin.</summary>
    public uint Version { get; private set; }

    public List<WorkItemTask> Tasks { get; private set; } = [];
    /// <summary>Histórico — no rastreamento só os registros novos (a leitura é por consulta própria).</summary>
    public List<WorkItemLog> Logs { get; private set; } = [];

    protected WorkItem() { }

    public WorkItem(string cardNumber, string? type, string? title, string? description, string origin, string? templateKey,
        Guid? planId, IReadOnlyList<WorkTaskInput> tasks, Guid? createdBy, string createdByName, DateTimeOffset now)
    {
        var card = (cardNumber ?? string.Empty).Trim();
        if (card.Length == 0 || card.Length > 100)
            throw new DomainException("Informe o número do card.");
        var o = (origin ?? string.Empty).Trim().ToLowerInvariant();
        if (!WorkItemOrigin.All.Contains(o))
            throw new DomainException($"Origem de item inválida: '{origin}'.");

        Id = Guid.NewGuid();
        CardNumber = card;
        Type = WorkItemType.Normalize(type);
        Origin = o;
        TemplateKey = Clean(templateKey, MaxTemplateKeyLength);
        PlanId = planId;
        CreatedBy = createdBy;
        CreatedByName = Clean(createdByName, 200) ?? "Usuário";
        CreatedAt = now;
        UpdatedAt = now;
        SetTitle(title);
        SetDescription(description);
        ReplaceTasks(tasks, now);
        AddLog(null, CreatedByName, WorkItemLogKind.Created, $"Item criado ({OriginLabel(o)}) com {Tasks.Count} sub-tarefa(s).", now);
    }

    public IReadOnlyList<WorkItemLink> Links
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LinksJson)) return [];
            try { return JsonSerializer.Deserialize<List<WorkItemLink>>(LinksJson, JsonOptions) ?? []; }
            catch (JsonException) { return []; }
        }
    }

    /// <summary>Pode editar: item pendente com todas as sub-tarefas pendentes.</summary>
    public bool Editable => Status == WorkItemStatus.Pending && Tasks.All(t => t.Status == WorkTaskStatus.Pending);

    public IEnumerable<WorkItemTask> OrderedTasks => Tasks.OrderBy(t => t.Order);

    // ---------------------------------------------------------------- edição

    public void Update(string? title, string? description, IReadOnlyList<WorkTaskInput>? tasks, string actor, DateTimeOffset now)
    {
        EnsureEditable();
        SetTitle(title);
        SetDescription(description);
        if (tasks is not null)
            ReplaceTasks(tasks, now);
        UpdatedAt = now;
        AddLog(null, actor, WorkItemLogKind.Updated, "Item editado.", now);
    }

    public void EnsureEditable()
    {
        if (!Editable)
            throw new WorkItemNotEditableException(Number);
    }

    /// <summary>Copia como item novo (pendente), para "Criar novo a partir deste".</summary>
    public WorkItem Duplicate(Guid? createdBy, string createdByName, DateTimeOffset now)
    {
        var tasks = OrderedTasks.Select(t => new WorkTaskInput
        {
            Title = t.Title,
            Description = t.Description,
            Kind = t.Kind,
            Executor = t.Executor,
            Params = new Dictionary<string, string>(t.Params),
        }).ToList();
        var copy = new WorkItem(CardNumber, Type, Title, Description, WorkItemOrigin.User, TemplateKey, null, tasks,
            createdBy, createdByName, now);
        copy.AddLog(null, createdByName, WorkItemLogKind.Info, $"Criado a partir do item #{Number}.", now);
        return copy;
    }

    private void SetTitle(string? title)
    {
        var t = (title ?? string.Empty).Trim();
        if (t.Length == 0)
            throw new DomainException("O título do item é obrigatório.");
        Title = t.Length <= MaxTitleLength ? t : t[..MaxTitleLength];
    }

    private void SetDescription(string? description)
    {
        var d = description?.Trim();
        if (d is { Length: > MaxDescriptionLength })
            throw new DomainException($"A descrição do item pode ter no máximo {MaxDescriptionLength} caracteres.");
        Description = string.IsNullOrEmpty(d) ? null : d;
    }

    private void ReplaceTasks(IReadOnlyList<WorkTaskInput> inputs, DateTimeOffset now)
    {
        if (inputs.Count > MaxTasks)
            throw new DomainException($"Um item pode ter no máximo {MaxTasks} sub-tarefas.");
        var kept = new List<WorkItemTask>();
        var order = 1;
        foreach (var input in inputs)
        {
            var existing = input.Number is { } n && n > 0 ? Tasks.FirstOrDefault(t => t.Number == n) : null;
            if (existing is not null && !kept.Contains(existing))
            {
                existing.Reshape(order, input);
                kept.Add(existing);
            }
            else
            {
                kept.Add(new WorkItemTask(Id, order, input, now));
            }
            order++;
        }
        Tasks.RemoveAll(t => !kept.Contains(t));
        foreach (var t in kept.Where(t => !Tasks.Contains(t)))
            Tasks.Add(t);
        Fingerprint = ComputeFingerprint(Type, OrderedTasks.Select(t => (t.Kind, (IReadOnlyDictionary<string, string>)t.Params)));
    }

    // ---------------------------------------------------------------- plano → item

    /// <summary>
    /// Acrescenta sub-tarefas para etapas novas do plano (por <c>StepKey</c>) enquanto o item não estiver fechado.
    /// Devolve as criadas.
    /// </summary>
    public List<WorkItemTask> AddStepTasks(IEnumerable<WorkTaskInput> steps, DateTimeOffset now)
    {
        var added = new List<WorkItemTask>();
        if (WorkItemStatus.IsClosed(Status) || Status == WorkItemStatus.Reverting) return added;
        var order = Tasks.Count == 0 ? 0 : Tasks.Max(t => t.Order);
        foreach (var s in steps)
        {
            if (string.IsNullOrWhiteSpace(s.StepKey) || Tasks.Any(t => t.StepKey == s.StepKey)) continue;
            if (Tasks.Count >= MaxTasks) break;
            var task = new WorkItemTask(Id, ++order, s, now);
            Tasks.Add(task);
            added.Add(task);
        }
        if (added.Count > 0)
        {
            Fingerprint = ComputeFingerprint(Type, OrderedTasks.Select(t => (t.Kind, (IReadOnlyDictionary<string, string>)t.Params)));
            UpdatedAt = now;
        }
        return added;
    }

    /// <summary>Reordena as sub-tarefas do plano pela ordem das etapas (as sem etapa ficam no fim, na ordem atual).</summary>
    public void ReorderByStep(IReadOnlyDictionary<string, int> stepOrder)
    {
        var ordered = OrderedTasks
            .OrderBy(t => t.StepKey is not null && stepOrder.TryGetValue(t.StepKey, out var o) ? o : int.MaxValue)
            .ThenBy(t => t.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Order = i + 1;
    }

    // ---------------------------------------------------------------- execução

    /// <summary>Entrou na fila (pedido <c>work-item</c> ou retomada do plano).</summary>
    public void MarkQueued(Guid? requestId, string actor, DateTimeOffset now)
    {
        if (WorkItemStatus.IsClosed(Status) || Status == WorkItemStatus.Reverting)
            throw new DomainException($"O item #{Number} está {Status} — não dá para executar.");
        ActiveRequestId = requestId ?? ActiveRequestId;
        if (Status is WorkItemStatus.Pending or WorkItemStatus.Failed)
            Status = WorkItemStatus.Queued;
        UpdatedAt = now;
        AddLog(null, actor, WorkItemLogKind.Request, requestId is null ? "Execução pedida (retomando o plano)." : "Execução pedida (na fila).", now);
    }

    /// <summary>O pedido de execução do item mudou (fila → rodando → terminou). Devolve se mudou algo.</summary>
    public bool AttachRequest(Guid requestId, string requestStatus, DateTimeOffset now)
    {
        var changed = false;
        if (ExecutionRequestStatus.IsActive(requestStatus))
        {
            if (ActiveRequestId != requestId) { ActiveRequestId = requestId; changed = true; }
            if (requestStatus == ExecutionRequestStatus.Running && Status is WorkItemStatus.Queued or WorkItemStatus.Pending)
            {
                Status = WorkItemStatus.Running;
                StartedAt ??= now;
                changed = true;
            }
        }
        string? unfinished = null;
        if (!ExecutionRequestStatus.IsActive(requestStatus) && ActiveRequestId == requestId)
        {
            ActiveRequestId = null;
            changed = true;
            // Pedido terminou (done/failed/expired/cancelled) sem o item fechar e sem sub-tarefa esperando alguém: falhou —
            // a pessoa vê e decide (executar de novo, cancelar, desfazer). Desfazer que terminou ainda "reverting" idem.
            var waiting = Tasks.Any(t => t.Status == WorkTaskStatus.Waiting);
            if (Status is WorkItemStatus.Queued or WorkItemStatus.Running && !waiting)
            {
                Status = WorkItemStatus.Failed;
                unfinished = $"A execução terminou ({requestStatus}) sem concluir as sub-tarefas.";
            }
            else if (Status == WorkItemStatus.Reverting)
            {
                Status = WorkItemStatus.Failed;
                unfinished = $"O desfazer terminou ({requestStatus}) sem desfazer todas as sub-tarefas.";
            }
            else if (Status is WorkItemStatus.Queued or WorkItemStatus.Running && waiting)
                Status = WorkItemStatus.Waiting;
        }
        if (changed)
        {
            UpdatedAt = now;
            AddLog(null, "Fila", WorkItemLogKind.Request, $"Pedido de execução {requestStatus}.", now);
            if (unfinished is not null)
                AddLog(null, "Fila", WorkItemLogKind.Status, unfinished, now);
        }
        return changed;
    }

    /// <summary>Ação sobre uma sub-tarefa (start/complete/fail/wait/skip/revert/cancel); recalcula o status do item.</summary>
    public WorkItemTask ApplyTaskAction(long taskNumber, string action, string? result, string? outputJson, WorkTaskUndo? undo,
        string actor, DateTimeOffset now)
    {
        var task = Tasks.FirstOrDefault(t => t.Number == taskNumber)
                   ?? throw new DomainException($"Sub-tarefa #{taskNumber} não é do item #{Number}.");
        var a = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (!WorkTaskAction.All.Contains(a))
            throw new DomainException($"Ação inválida: '{action}' (use start, complete, fail, wait, skip, revert ou cancel).");

        if (Status == WorkItemStatus.Reverting)
        {
            if (a is not (WorkTaskAction.Revert or WorkTaskAction.Fail))
                throw new DomainException($"O item #{Number} está sendo desfeito — use revert ou fail.");
        }
        else if (a == WorkTaskAction.Revert)
            throw new DomainException($"Só dá para desfazer sub-tarefa durante o desfazer do item (item #{Number} está {Status}).");
        else if (Status is WorkItemStatus.Cancelled or WorkItemStatus.Reverted)
            throw new DomainException($"O item #{Number} está {Status} — a sub-tarefa não pode mais mudar.");

        var undoFailed = Status == WorkItemStatus.Reverting && a == WorkTaskAction.Fail;
        if (undoFailed)
            task.NoteUndoFailure(result, now); // a sub-tarefa continua concluída: o que ela fez não foi desfeito
        else
            task.Apply(a, result, outputJson, undo, now);
        if (a is WorkTaskAction.Start)
            StartedAt ??= now;
        AddLog(task.Number, actor, WorkItemLogKind.Task, $"#{task.Number} {task.Title}: {task.Status}" +
            (string.IsNullOrWhiteSpace(result) ? string.Empty : $" — {Short(result!, 300)}"), now);
        Recalculate(actor, now, failedUndo: undoFailed);
        UpdatedAt = now;
        return task;
    }

    /// <summary>Resultado, saídas e "como desfazer" sem mudar o status (sub-tarefa espelho do plano: o status vem da etapa).</summary>
    public void AnnotateTask(long taskNumber, string? result, string? outputJson, WorkTaskUndo? undo, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(result) && outputJson is null && undo is null) return;
        var task = Tasks.FirstOrDefault(t => t.Number == taskNumber)
                   ?? throw new DomainException($"Sub-tarefa #{taskNumber} não é do item #{Number}.");
        task.Annotate(result, outputJson, undo, now);
        UpdatedAt = now;
    }

    /// <summary>
    /// Espelha o status de uma etapa do plano na sub-tarefa (sem validar a transição — o plano é a fonte). Devolve se mudou.
    /// </summary>
    public bool SyncStep(string stepKey, string stepStatus, string? reason, string actor, DateTimeOffset now)
    {
        if (WorkItemStatus.IsClosed(Status) || Status == WorkItemStatus.Reverting) return false;
        var task = Tasks.FirstOrDefault(t => t.StepKey == stepKey);
        if (task is null) return false;
        var target = stepStatus switch
        {
            ExecutionStatus.Running => WorkTaskStatus.Running,
            ExecutionStatus.Waiting => WorkTaskStatus.Waiting,
            ExecutionStatus.Completed => WorkTaskStatus.Completed,
            ExecutionStatus.Failed => WorkTaskStatus.Failed,
            ExecutionStatus.Cancelled => WorkTaskStatus.Skipped,
            _ => WorkTaskStatus.Pending,
        };
        if (task.Status == target) return false;
        task.Force(target, reason, now);
        if (target is WorkTaskStatus.Running or WorkTaskStatus.Waiting) StartedAt ??= now;
        AddLog(task.Number, actor, WorkItemLogKind.Task, $"#{task.Number} {task.Title}: {task.Status} (etapa do plano)", now);
        Recalculate(actor, now, failedUndo: false);
        UpdatedAt = now;
        return true;
    }

    /// <summary>Plano concluído/cancelado: fecha o item.</summary>
    public bool ClosePlan(string planStatus, string? reason, string actor, DateTimeOffset now)
    {
        if (WorkItemStatus.IsClosed(Status) || Status == WorkItemStatus.Reverting) return false;
        if (planStatus == ExecutionStatus.Completed)
        {
            foreach (var t in Tasks.Where(t => WorkTaskStatus.IsOpen(t.Status) || t.Status == WorkTaskStatus.Failed))
                t.Force(WorkTaskStatus.Skipped, "Plano concluído", now);
            SetStatus(WorkItemStatus.Completed, actor, "Plano concluído.", now);
            return true;
        }
        if (planStatus == ExecutionStatus.Cancelled)
        {
            Cancel(reason ?? "Plano cancelado", actor, now);
            return true;
        }
        return false;
    }

    /// <summary>Cancela o item e as sub-tarefas abertas.</summary>
    public void Cancel(string? reason, string actor, DateTimeOffset now)
    {
        if (Status is WorkItemStatus.Cancelled or WorkItemStatus.Reverted or WorkItemStatus.Reverting)
            throw new DomainException($"O item #{Number} já está {Status}.");
        if (Status == WorkItemStatus.Completed)
            throw new DomainException($"O item #{Number} já foi concluído — use desfazer.");
        var r = Clean(reason, MaxReasonLength);
        foreach (var t in Tasks.Where(t => WorkTaskStatus.IsOpen(t.Status)))
            t.Force(WorkTaskStatus.Cancelled, r ?? "Item cancelado", now);
        CancelReason = r;
        ActiveRequestId = null;
        SetStatus(WorkItemStatus.Cancelled, actor, r is null ? "Item cancelado." : $"Item cancelado: {r}", now);
    }

    /// <summary>Sub-tarefas concluídas que serão desfeitas (ordem reversa).</summary>
    public IReadOnlyList<WorkItemTask> TasksToUndo =>
        OrderedTasks.Where(t => t.Status == WorkTaskStatus.Completed).Reverse().ToList();

    /// <summary>Começa a desfazer (item cancelado, concluído ou falho). Sem nada a desfazer → já fica <c>reverted</c>.</summary>
    public void StartUndo(string actor, DateTimeOffset now)
    {
        if (Status is not (WorkItemStatus.Cancelled or WorkItemStatus.Completed or WorkItemStatus.Failed))
            throw new DomainException($"Só dá para desfazer item cancelado, concluído ou com falha (item #{Number} está {Status}).");
        if (TasksToUndo.Count == 0)
        {
            SetStatus(WorkItemStatus.Reverted, actor, "Nada a desfazer — item desfeito.", now);
            return;
        }
        SetStatus(WorkItemStatus.Reverting, actor, $"Desfazendo {TasksToUndo.Count} sub-tarefa(s).", now);
        AddLog(null, actor, WorkItemLogKind.Undo, "Desfazer pedido.", now);
    }

    public void AddLinks(IEnumerable<WorkItemLink>? links, DateTimeOffset now)
    {
        if (links is null) return;
        var list = Links.ToList();
        var changed = false;
        foreach (var l in links)
        {
            var url = l.Url?.Trim();
            if (string.IsNullOrEmpty(url) || url.Length > MaxLinkUrlLength) continue;
            if (list.Any(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase))) continue;
            var kind = (l.Kind ?? string.Empty).Trim().ToLowerInvariant();
            list.Add(new WorkItemLink
            {
                Kind = WorkItemLinkKind.All.Contains(kind) ? kind : WorkItemLinkKind.Document,
                Title = Clean(l.Title, 300),
                Url = url,
            });
            changed = true;
        }
        if (!changed) return;
        LinksJson = JsonSerializer.Serialize(list.TakeLast(MaxLinks).ToList(), JsonOptions);
        UpdatedAt = now;
    }

    public void AddLog(long? taskNumber, string actor, string kind, string text, DateTimeOffset now) =>
        Logs.Add(new WorkItemLog(Id, taskNumber, actor, kind, text, now));

    private void Recalculate(string actor, DateTimeOffset now, bool failedUndo)
    {
        if (Status == WorkItemStatus.Reverting)
        {
            if (failedUndo)
            {
                SetStatus(WorkItemStatus.Failed, actor, "Falha ao desfazer.", now);
                return;
            }
            if (Tasks.All(t => t.Status != WorkTaskStatus.Completed))
            {
                ActiveRequestId = null;
                SetStatus(WorkItemStatus.Reverted, actor, "Item desfeito.", now);
            }
            return;
        }
        if (Status is WorkItemStatus.Cancelled or WorkItemStatus.Reverted) return;

        var live = Tasks.Where(t => t.Status is not (WorkTaskStatus.Cancelled or WorkTaskStatus.Reverted)).ToList();
        string next;
        if (live.Count > 0 && live.All(t => WorkTaskStatus.IsDone(t.Status))) next = WorkItemStatus.Completed;
        else if (live.Any(t => t.Status == WorkTaskStatus.Running)) next = WorkItemStatus.Running;
        else if (live.Any(t => t.Status == WorkTaskStatus.Waiting)) next = WorkItemStatus.Waiting;
        else if (live.Any(t => t.Status == WorkTaskStatus.Failed)) next = WorkItemStatus.Failed;
        else if (live.Any(t => WorkTaskStatus.IsDone(t.Status))) next = WorkItemStatus.Running;
        else next = ActiveRequestId is not null ? WorkItemStatus.Queued
            : Status is WorkItemStatus.Queued ? WorkItemStatus.Queued : WorkItemStatus.Pending;
        SetStatus(next, actor, null, now);
    }

    private void SetStatus(string status, string actor, string? text, DateTimeOffset now)
    {
        if (Status == status) return;
        var previous = Status;
        Status = status;
        if (status is WorkItemStatus.Running or WorkItemStatus.Waiting) StartedAt ??= now;
        if (status is WorkItemStatus.Completed or WorkItemStatus.Cancelled or WorkItemStatus.Reverted)
            CompletedAt = now;
        else if (status is not WorkItemStatus.Failed)
            CompletedAt = null;
        if (status is WorkItemStatus.Completed or WorkItemStatus.Reverted)
            ActiveRequestId = null;
        UpdatedAt = now;
        AddLog(null, actor, WorkItemLogKind.Status, text ?? $"Status: {previous} → {status}.", now);
    }

    // ---------------------------------------------------------------- fingerprint

    /// <summary>Parâmetros que identificam o que a sub-tarefa faz (o resto — mensagem, descrição — não conta).</summary>
    public static readonly IReadOnlyList<string> FingerprintParams = ["repository", "source", "target", "branch"];

    /// <summary>
    /// sha256 (hex minúsculo) de <c>tipo|kind1,kind2|repository=..;source=..;target=..;branch=..|...</c>, tudo
    /// minúsculo e sem espaços nas pontas.
    /// </summary>
    public static string ComputeFingerprint(string type, IEnumerable<(string Kind, IReadOnlyDictionary<string, string> Params)> tasks)
    {
        var list = tasks.ToList();
        var sb = new StringBuilder();
        sb.Append(Norm(type)).Append('|');
        sb.Append(string.Join(',', list.Select(t => Norm(t.Kind))));
        foreach (var t in list)
        {
            sb.Append('|');
            var dict = t.Params.ToDictionary(p => Norm(p.Key), p => Norm(p.Value));
            sb.Append(string.Join(';', FingerprintParams.Select(k => $"{k}={(dict.TryGetValue(k, out var v) ? v : string.Empty)}")));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();

        static string Norm(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string OriginLabel(string origin) => origin switch
    {
        WorkItemOrigin.Specialist => "pelo especialista",
        WorkItemOrigin.Analysis => "pela análise",
        WorkItemOrigin.Correction => "pelo plano de correção",
        WorkItemOrigin.Template => "de um modelo",
        _ => "pelo usuário",
    };

    private static string Short(string s, int max)
    {
        var t = s.Trim().ReplaceLineEndings(" ");
        return t.Length <= max ? t : t[..max] + "…";
    }

    internal static string? Clean(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }
}

/// <summary>0085: item iniciado/concluído — não edita (a tela oferece "Criar novo a partir deste").</summary>
public class WorkItemNotEditableException(long number)
    : DomainException($"O item #{number} já começou ou terminou — só dá para editar item pendente. Crie um novo a partir dele.")
{
    public long Number { get; } = number;
}

/// <summary>0085: já existe item igual (mesmo fingerprint) no card.</summary>
public class WorkItemDuplicateException(long number, string title, string status)
    : DomainException($"Já existe o item #{number} \"{title}\" ({status}) igual a este no card. Use o existente ou informe allowDuplicate com o motivo.")
{
    public long ExistingNumber { get; } = number;
    public string ExistingTitle { get; } = title;
    public string ExistingStatus { get; } = status;
}
