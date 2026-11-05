using System.Text;
using System.Text.Json;
using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0085: itens de trabalho do card. A fila (<see cref="IWorkItemQueue"/>), o plano (<see cref="IExecutionPlanApplication"/>)
/// e a retomada (<see cref="IExecutionResumeTrigger"/>) são resolvidos sob demanda — eles também chamam esta classe
/// (sincronizar o plano, anexar o pedido), e a injeção direta fecharia um ciclo na DI.
/// </summary>
public class WorkItemApplication : IWorkItemApplication
{
    private const int ConcurrencyRetries = 4;
    private const int MaxLogs = 500;
    /// <summary>Plano de análise de User Story (a fila escolhe a skill analisar-us).</summary>
    public const string UserStoryPlanKind = "analisar-us";

    private readonly IWorkItemRepository _items;
    private readonly IExecutionPlanRepository _plans;
    private readonly IRealTimeNotifier _realTime;
    private readonly IExecutionTimelineWriter _timeline;
    private readonly IServiceProvider _services;

    public WorkItemApplication(IWorkItemRepository items, IExecutionPlanRepository plans, IRealTimeNotifier realTime,
        IExecutionTimelineWriter timeline, IServiceProvider services)
    {
        _items = items;
        _plans = plans;
        _realTime = realTime;
        _timeline = timeline;
        _services = services;
    }

    private IWorkItemQueue Queue => (IWorkItemQueue?)_services.GetService(typeof(IWorkItemQueue))
                                    ?? throw new DomainException("A fila dos itens de trabalho não está disponível neste servidor.");
    private IExecutionPlanApplication PlanApp => (IExecutionPlanApplication)_services.GetService(typeof(IExecutionPlanApplication))!;
    private IExecutionResumeTrigger? ResumeTrigger => (IExecutionResumeTrigger?)_services.GetService(typeof(IExecutionResumeTrigger));
    private IExecutionQueueRepository? QueueRepository => (IExecutionQueueRepository?)_services.GetService(typeof(IExecutionQueueRepository));

    // ================================================================ leitura

    public async Task<IReadOnlyList<WorkItemResponse>> ListAsync(string card, CancellationToken ct)
    {
        var items = await _items.ListByCardAsync(NormalizeCard(card), ct);
        return await ToResponsesAsync(items, ct);
    }

    public async Task<WorkItemResponse> GetAsync(long number, bool withLogs, CancellationToken ct)
    {
        var item = await _items.GetByNumberAsync(number, ct) ?? throw NotFound(number);
        var response = (await ToResponsesAsync([item], ct))[0];
        if (withLogs)
            response.Logs = (await _items.GetLogsAsync(item.Id, MaxLogs, ct)).Select(WorkItemLogResponse.From).ToList();
        return response;
    }

    public async Task<WorkItemResponse?> GetByIdAsync(Guid workItemId, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(workItemId, ct);
        return item is null ? null : (await ToResponsesAsync([item], ct))[0];
    }

    public async Task<string> SummaryMarkdownAsync(string card, CancellationToken ct)
    {
        var items = await _items.ListByCardAsync(NormalizeCard(card), ct);
        var sb = new StringBuilder();
        sb.AppendLine($"## Itens de trabalho do card ({items.Count})");
        if (items.Count == 0)
        {
            sb.AppendLine("Nenhum item de trabalho ainda. Antes de criar um, confira que não repete o que o plano já faz.");
            return sb.ToString();
        }
        sb.AppendLine("Não crie item igual a um destes (mesmo tipo, sub-tarefas e repositório/origem/destino/branch = mesmo fingerprint).");
        foreach (var item in items)
        {
            var progress = $"{item.Tasks.Count(t => WorkTaskStatus.IsDone(t.Status))}/{item.Tasks.Count}";
            sb.AppendLine();
            sb.AppendLine($"- **#{item.Number}** [{item.Type}] {item.Title} — **{item.Status}** ({progress}) · origem: {item.Origin}" +
                          (item.PlanId is null ? string.Empty : " · ligado ao plano") +
                          (item.TemplateKey is null ? string.Empty : $" · modelo: {item.TemplateKey}") +
                          $" · fingerprint: `{item.Fingerprint}`");
            if (item.CancelReason is not null)
                sb.AppendLine($"  - motivo do cancelamento: {item.CancelReason}");
            foreach (var t in item.OrderedTasks)
            {
                var keyParams = t.Params.Where(p => WorkItem.FingerprintParams.Contains(p.Key.ToLowerInvariant()) && p.Value.Length > 0)
                    .Select(p => $"{p.Key}={p.Value}").ToList();
                var line = $"  - #{t.Number} `{t.Kind}` {t.Title} — {t.Status}" +
                           (t.Executor == ExecutionExecutor.User ? " (usuário)" : string.Empty) +
                           (keyParams.Count > 0 ? $" · {string.Join(", ", keyParams)}" : string.Empty);
                var outputs = OutputSummary(t.OutputElement);
                if (outputs.Length > 0) line += $" · saídas: {outputs}";
                sb.AppendLine(line);
            }
            foreach (var link in item.Links.Where(l => l.Kind is WorkItemLinkKind.PullRequest or WorkItemLinkKind.Branch))
                sb.AppendLine($"  - {link.Kind}: {link.Title ?? link.Url} ({link.Url})");
        }
        return sb.ToString();
    }

    // ================================================================ criar / editar

    public async Task<WorkItemResponse> CreateAsync(string card, CreateWorkItemRequest req, string origin, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var cardNumber = NormalizeCard(card);
        var o = (origin ?? WorkItemOrigin.User).Trim().ToLowerInvariant();
        if (o == WorkItemOrigin.User && !string.IsNullOrWhiteSpace(req.TemplateKey))
            o = WorkItemOrigin.Template;

        Guid? planId = null;
        if (req.PlanId is { } pid)
        {
            var plan = await _plans.GetPlanWithStepsAsync(pid, ct) ?? throw new ExecutionPlanNotFoundException("Plano não encontrado.");
            if (!string.Equals(plan.CardNumber, cardNumber, StringComparison.Ordinal))
                throw new DomainException("O plano informado é de outro card.");
            planId = pid;
        }

        var type = string.IsNullOrWhiteSpace(req.Type) ? await DefaultTypeAsync(cardNumber, ct) : req.Type;
        var now = DateTimeOffset.UtcNow;
        var item = new WorkItem(cardNumber, type, req.Title, req.Description, o, req.TemplateKey, planId, req.Tasks ?? [],
            actor.UserId, actor.Name, now);

        var existing = await _items.FindDuplicateAsync(cardNumber, item.Fingerprint, null, ct);
        if (existing is not null)
        {
            if (!req.AllowDuplicate)
                throw new WorkItemDuplicateException(existing.Number, existing.Title, existing.Status);
            if (string.IsNullOrWhiteSpace(req.DuplicateReason))
                throw new DomainException($"Já existe o item #{existing.Number} igual a este: informe o motivo (duplicateReason) para criar outro.");
            item.AddLog(null, actor.Name, WorkItemLogKind.Info,
                $"Criado mesmo igual ao item #{existing.Number}: {req.DuplicateReason.Trim()}", now);
        }

        _items.Add(item);
        await _items.SaveChangesAsync(ct);
        await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Created, null, ct);
        await TimelineAsync(item, $"Item de trabalho **#{item.Number}** criado ({OriginText(item.Origin)}): {item.Title} — {item.Tasks.Count} sub-tarefa(s).", actor, ct);
        return (await ToResponsesAsync([item], ct))[0];
    }

    public async Task<WorkItemResponse> UpdateAsync(long number, UpdateWorkItemRequest req, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var item = await MutateAsync(() => _items.GetByNumberAsync(number, ct), number,
            i => i.Update(req.Title ?? i.Title, req.Description, req.Tasks, actor.Name, DateTimeOffset.UtcNow), ct);
        await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Updated, null, ct);
        return (await ToResponsesAsync([item], ct))[0];
    }

    public async Task<WorkItemResponse> DuplicateAsync(long number, ExecutionActor actor, CancellationToken ct)
    {
        var source = await _items.GetByNumberAsync(number, ct) ?? throw NotFound(number);
        var copy = source.Duplicate(actor.UserId, actor.Name, DateTimeOffset.UtcNow);
        _items.Add(copy);
        await _items.SaveChangesAsync(ct);
        await NotifyAsync(copy, ExecutionPlanRealTimeEvents.WorkItemActions.Created, null, ct);
        await TimelineAsync(copy, $"Item de trabalho **#{copy.Number}** criado a partir do #{source.Number}: {copy.Title}.", actor, ct);
        return (await ToResponsesAsync([copy], ct))[0];
    }

    // ================================================================ sub-tarefas

    public async Task<WorkItemResponse> TaskActionAsync(long taskNumber, string action, WorkTaskActionRequest req, ExecutionActor actor, CancellationToken ct)
    {
        req ??= new WorkTaskActionRequest();
        var a = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (!WorkTaskAction.All.Contains(a))
            throw new DomainException($"Ação inválida: '{action}' (use start, complete, fail, wait, skip, revert ou cancel).");
        var item = await _items.GetByTaskNumberAsync(taskNumber, ct)
                   ?? throw new ExecutionPlanNotFoundException($"Sub-tarefa #{taskNumber} não encontrada.");
        var task = item.Tasks.First(t => t.Number == taskNumber);
        var before = item.Status;

        // Sub-tarefa espelho de uma etapa do plano: o plano é a fonte — a ação vai para a etapa e volta pela sincronização.
        if (item.PlanId is { } planId && task.StepKey is { } stepKey && a != WorkTaskAction.Revert
            && item.Status is not (WorkItemStatus.Reverting or WorkItemStatus.Cancelled or WorkItemStatus.Reverted))
        {
            await ApplyOnPlanStepAsync(planId, stepKey, a, req.Result, actor, ct);
            _items.DetachWorkItems();
            item = await MutateAsync(() => _items.GetByIdAsync(item.Id, ct), item.Number, i =>
            {
                var now = DateTimeOffset.UtcNow;
                i.AnnotateTask(taskNumber, req.Result, Serialize(req.Output), req.Undo, now);
                i.AddLinks(req.Links, now);
            }, ct);
        }
        else
        {
            item = await MutateAsync(() => _items.GetByTaskNumberAsync(taskNumber, ct), item.Number, i =>
            {
                var now = DateTimeOffset.UtcNow;
                i.ApplyTaskAction(taskNumber, a, req.Result, Serialize(req.Output), req.Undo, actor.Name, now);
                i.AddLinks(req.Links, now);
            }, ct);
        }

        await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Task, taskNumber, ct);
        await AfterStatusChangeAsync(item, before, actor, ct);
        return (await ToResponsesAsync([item], ct))[0];
    }

    private async Task ApplyOnPlanStepAsync(Guid planId, string stepKey, string action, string? result, ExecutionActor actor, CancellationToken ct)
    {
        var plans = PlanApp;
        switch (action)
        {
            case WorkTaskAction.Start:
                await plans.StartStepAsync(planId, stepKey, actor, ct);
                break;
            case WorkTaskAction.Complete:
                await plans.CompleteStepAsync(planId, stepKey, result, actor, ct);
                break;
            case WorkTaskAction.Skip:
            case WorkTaskAction.Cancel:
                await plans.CancelStepAsync(planId, stepKey, string.IsNullOrWhiteSpace(result) ? $"Pulada pelo item de trabalho ({actor.Name})" : result, actor, ct);
                break;
            case WorkTaskAction.Fail:
                await plans.UpdateStepAsync(planId, stepKey, new UpdateExecutionStepRequest
                {
                    Status = ExecutionStatus.Failed,
                    Reason = string.IsNullOrWhiteSpace(result) ? "Falhou" : result,
                }, actor, ct);
                break;
            case WorkTaskAction.Wait:
                await plans.UpdateStepAsync(planId, stepKey, new UpdateExecutionStepRequest
                {
                    Status = ExecutionStatus.Waiting,
                    Reason = result,
                    WaitingOn = ExecutionWaitingOn.External,
                }, actor, ct);
                break;
        }
    }

    // ================================================================ executar / cancelar / desfazer

    public async Task<WorkItemResponse> ExecuteAsync(long number, ExecutionActor actor, CancellationToken ct)
    {
        var item = await _items.GetByNumberAsync(number, ct) ?? throw NotFound(number);
        if (WorkItemStatus.IsClosed(item.Status) || item.Status == WorkItemStatus.Reverting)
            throw new DomainException($"O item #{item.Number} está {item.Status} — não dá para executar. Crie um novo a partir dele.");
        if (item.Tasks.Count == 0)
            throw new DomainException($"O item #{item.Number} não tem sub-tarefas.");

        Guid? requestId = null;
        if (item.PlanId is { } planId)
        {
            await ResumePlanAsync(planId, actor, ct);
        }
        else
        {
            if (item.ActiveRequestId is not null)
                return (await ToResponsesAsync([item], ct))[0];
            requestId = await Queue.EnqueueAsync(item, actor, ct);
        }

        _items.DetachWorkItems();
        item = await MutateAsync(() => _items.GetByNumberAsync(number, ct), number,
            i => i.MarkQueued(requestId, actor.Name, DateTimeOffset.UtcNow), ct);
        await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Request, null, ct);
        return (await ToResponsesAsync([item], ct))[0];
    }

    /// <summary>Item ligado a plano roda retomando o plano (pista main de hoje).</summary>
    private async Task ResumePlanAsync(Guid planId, ExecutionActor actor, CancellationToken ct)
    {
        var plan = await _plans.GetPlanWithStepsAsync(planId, ct) ?? throw new ExecutionPlanNotFoundException("Plano do item não encontrado.");
        if (plan.Status is ExecutionStatus.Cancelled or ExecutionStatus.Completed)
            throw new DomainException($"O plano do item está {plan.Status} — crie um novo item a partir deste.");
        if (plan.Status == ExecutionStatus.Paused || plan.Status == ExecutionStatus.Failed)
        {
            // "Continuar" o plano: a mudança de status já pede a retomada ao executor do dono.
            await PlanApp.ChangeStatusAsync(planId, new ChangeExecutionPlanStatusRequest { Status = ExecutionStatus.Running }, actor, ct);
            return;
        }
        var request = ResumeTrigger is { } trigger
            ? await trigger.PlanChangedAsync(plan, ExecutionRequestSource.Button, actor, explicitRequest: true, ct)
            : null;
        if (request is null && QueueRepository is { } queue && await queue.GetActiveRequestForCardAsync(plan.CardNumber, ct) is null)
            throw new DomainException("Não deu para retomar o plano: o dono do plano não tem executor ligado (ou o plano acabou de rodar). Tente de novo em instantes.");
    }

    public async Task<WorkItemResponse> CancelAsync(long number, bool undo, string? reason, ExecutionActor actor, CancellationToken ct)
    {
        var item = await _items.GetByNumberAsync(number, ct) ?? throw NotFound(number);
        var before = item.Status;
        if (item.Status == WorkItemStatus.Completed && !undo)
            throw new DomainException($"O item #{item.Number} já foi concluído — para voltar o que ele fez, use desfazer.");
        if (item.ActiveRequestId is not null)
            await CancelRequestAsync(item.Id, actor, ct);

        _items.DetachWorkItems();
        item = await MutateAsync(() => _items.GetByNumberAsync(number, ct), number, i =>
        {
            if (i.Status is not (WorkItemStatus.Cancelled or WorkItemStatus.Completed))
                i.Cancel(reason, actor.Name, DateTimeOffset.UtcNow);
        }, ct);
        await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Status, null, ct);
        await AfterStatusChangeAsync(item, before, actor, ct);
        return undo ? await UndoAsync(number, actor, ct) : (await ToResponsesAsync([item], ct))[0];
    }

    public async Task<WorkItemResponse> UndoAsync(long number, ExecutionActor actor, CancellationToken ct)
    {
        var item = await _items.GetByNumberAsync(number, ct) ?? throw NotFound(number);
        if (item.ActiveRequestId is not null)
            await CancelRequestAsync(item.Id, actor, ct);
        _items.DetachWorkItems();
        item = await MutateAsync(() => _items.GetByNumberAsync(number, ct), number, i => i.StartUndo(actor.Name, DateTimeOffset.UtcNow), ct);
        await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Status, null, ct);
        return await EnqueueUndoAsync(item, actor, ct);
    }

    private async Task<WorkItemResponse> EnqueueUndoAsync(WorkItem item, ExecutionActor actor, CancellationToken ct)
    {
        if (item.Status == WorkItemStatus.Reverted)
        {
            await TimelineAsync(item, $"Item de trabalho **#{item.Number}** desfeito (nada a desfazer) por {actor.Name}.", actor, ct);
            return (await ToResponsesAsync([item], ct))[0];
        }
        var requestId = await Queue.EnqueueUndoAsync(item, actor, ct);
        var number = item.Number;
        _items.DetachWorkItems();
        item = await MutateAsync(() => _items.GetByNumberAsync(number, ct), number,
            i => i.AttachRequest(requestId, ExecutionRequestStatus.Queued, DateTimeOffset.UtcNow), ct);
        await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Request, null, ct);
        await TimelineAsync(item, $"Desfazendo o item de trabalho **#{item.Number}** ({item.TasksToUndo.Count} sub-tarefa(s)) — pedido por {actor.Name}.", actor, ct);
        return (await ToResponsesAsync([item], ct))[0];
    }

    private async Task CancelRequestAsync(Guid workItemId, ExecutionActor actor, CancellationToken ct)
    {
        if (_services.GetService(typeof(IWorkItemQueue)) is IWorkItemQueue queue)
            await queue.CancelActiveAsync(workItemId, actor, ct);
    }

    public async Task<IReadOnlyList<WorkItemResponse>> CancelByPlanAsync(Guid planId, bool undo, string? reason, ExecutionActor actor, CancellationToken ct)
    {
        var numbers = (await _items.GetByPlanAsync(planId, ct)).Select(i => i.Number).ToList();
        _items.DetachWorkItems();
        var result = new List<WorkItemResponse>();
        foreach (var number in numbers)
        {
            var item = await _items.GetByNumberAsync(number, ct);
            if (item is null) continue;
            var cancellable = item.Status is not (WorkItemStatus.Cancelled or WorkItemStatus.Completed or WorkItemStatus.Reverted or WorkItemStatus.Reverting);
            var undoable = undo && item.Status is WorkItemStatus.Cancelled or WorkItemStatus.Completed or WorkItemStatus.Failed;
            _items.DetachWorkItems();
            if (cancellable)
                result.Add(await CancelAsync(number, undo, reason ?? "Plano cancelado", actor, ct));
            else if (undoable)
                result.Add(await UndoAsync(number, actor, ct));
            else
                result.Add(await GetAsync(number, false, ct));
            _items.DetachWorkItems();
        }
        return result;
    }

    // ================================================================ plano → item

    public async Task<WorkItemResponse> FromPlanAsync(string card, Guid planId, ExecutionActor actor, CancellationToken ct)
    {
        var cardNumber = NormalizeCard(card);
        var existing = (await _items.GetByPlanAsync(planId, ct)).ToList();
        if (existing.Count > 0)
        {
            var mirror = existing.FirstOrDefault(IsMirror) ?? existing[0];
            return (await ToResponsesAsync([mirror], ct))[0];
        }
        var plan = await _plans.GetPlanWithStepsAsync(planId, ct) ?? throw new ExecutionPlanNotFoundException("Plano não encontrado.");
        if (!string.Equals(plan.CardNumber, cardNumber, StringComparison.Ordinal))
            throw new DomainException("O plano é de outro card.");

        var now = DateTimeOffset.UtcNow;
        var tasks = StepInputs(plan.Steps).ToList();
        var type = string.Equals(plan.Kind, UserStoryPlanKind, StringComparison.OrdinalIgnoreCase)
            ? WorkItemType.UserStory
            : await DefaultTypeAsync(cardNumber, ct);
        var origin = plan.Phase == ExecutionPhase.Correction ? WorkItemOrigin.Correction : WorkItemOrigin.Analysis;
        var description = plan.Summary is { Length: > WorkItem.MaxDescriptionLength } s ? s[..WorkItem.MaxDescriptionLength] : plan.Summary;
        var item = new WorkItem(cardNumber, type, plan.Title, description, origin, null, plan.Id, tasks,
            actor.UserId ?? plan.CreatedByUserId, actor.UserId is null ? plan.CreatedBy : actor.Name, now);
        _items.Add(item);
        try
        {
            await _items.SaveChangesAsync(ct);
            // Card antigo: o plano já andou — as sub-tarefas nascem com o status das etapas.
            SyncItem(item, plan, "Plano", now);
            await _items.SaveChangesAsync(ct);
        }
        catch
        {
            _items.DetachWorkItems();
            throw;
        }
        await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Created, null, ct);
        await TimelineAsync(item, $"Item de trabalho **#{item.Number}** criado do plano de {(plan.Phase == ExecutionPhase.Correction ? "correção" : "análise")}: {item.Title} — {item.Tasks.Count} sub-tarefa(s).", actor, ct);
        return (await ToResponsesAsync([item], ct))[0];
    }

    public async Task SyncFromPlanAsync(Guid planId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var items = await _items.GetByPlanAsync(planId, ct);
                if (items.Count == 0) return;
                var plan = await _plans.GetPlanWithStepsAsync(planId, ct);
                if (plan is null) return;

                var now = DateTimeOffset.UtcNow;
                var changes = new List<(WorkItem Item, string Before, Guid? ActiveRequest)>();
                foreach (var item in items)
                {
                    var before = item.Status;
                    var active = item.ActiveRequestId;
                    var version = item.UpdatedAt;
                    SyncItem(item, plan, "Plano", now);
                    if (item.UpdatedAt != version || item.Status != before)
                        changes.Add((item, before, active));
                }
                if (changes.Count == 0) return;
                await _items.SaveChangesAsync(ct);

                var actor = new ExecutionActor(null, "Plano", false);
                foreach (var (item, before, active) in changes)
                {
                    if (active is not null && item.Status == WorkItemStatus.Cancelled)
                        await TryAsync(() => CancelRequestAsync(item.Id, actor, ct));
                    await NotifyAsync(item, item.Status != before ? ExecutionPlanRealTimeEvents.WorkItemActions.Status
                        : ExecutionPlanRealTimeEvents.WorkItemActions.Task, null, ct);
                    await AfterStatusChangeAsync(item, before, actor, ct);
                }
                return;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < ConcurrencyRetries)
            {
                _items.DetachWorkItems();
            }
            catch
            {
                // Nada do item fica pendurado no contexto (o plano salva no mesmo contexto depois).
                _items.DetachWorkItems();
                throw;
            }
        }
    }

    /// <summary>Etapas → sub-tarefas (as novas entram no item espelho), status das etapas, plano concluído/cancelado.</summary>
    private static void SyncItem(WorkItem item, ExecutionPlan plan, string actor, DateTimeOffset now)
    {
        if (plan.Status is ExecutionStatus.Completed or ExecutionStatus.Cancelled)
        {
            // Concluído: as etapas que terminaram ficam espelhadas antes de fechar o item.
            if (plan.Status == ExecutionStatus.Completed)
                foreach (var step in plan.Steps.OrderBy(s => s.Order))
                    item.SyncStep(step.Key, step.Status, step.StatusReason, actor, now);
            item.ClosePlan(plan.Status, plan.StatusReason, actor, now);
            return;
        }
        if (IsMirror(item))
        {
            var added = item.AddStepTasks(StepInputs(plan.Steps), now);
            if (added.Count > 0)
            {
                item.ReorderByStep(plan.Steps.ToDictionary(s => s.Key, s => s.Order));
                item.AddLog(null, actor, WorkItemLogKind.Updated, $"{added.Count} sub-tarefa(s) nova(s) do plano: {string.Join(", ", added.Select(t => t.Title))}.", now);
            }
        }
        foreach (var step in plan.Steps.OrderBy(s => s.Order))
            item.SyncStep(step.Key, step.Status, step.StatusReason, actor, now);
    }

    /// <summary>Item que espelha as etapas do plano (gerado do plano).</summary>
    private static bool IsMirror(WorkItem item) =>
        item.Origin is WorkItemOrigin.Correction or WorkItemOrigin.Analysis && item.Tasks.Any(t => t.StepKey is not null)
        || item.Tasks.Count > 0 && item.Tasks.All(t => t.StepKey is not null);

    private static IEnumerable<WorkTaskInput> StepInputs(IEnumerable<ExecutionStep> steps) =>
        steps.OrderBy(s => s.Order).Select(s => new WorkTaskInput
        {
            Title = s.Title,
            Description = s.Description,
            Kind = WorkTaskKind.FromStep(s.Kind, s.Executor),
            Executor = s.Executor,
            Params = string.IsNullOrWhiteSpace(s.Repository) ? null : new Dictionary<string, string> { ["repository"] = s.Repository! },
            StepKey = s.Key,
        });

    // ================================================================ fila

    public async Task AttachRequestAsync(Guid workItemId, Guid requestId, string status, CancellationToken ct)
    {
        WorkItem? item = null;
        var changed = false;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                item = await _items.GetByIdAsync(workItemId, ct);
                if (item is null) return;
                changed = item.AttachRequest(requestId, status, DateTimeOffset.UtcNow);
                if (!changed) return;
                await _items.SaveChangesAsync(ct);
                break;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < ConcurrencyRetries)
            {
                _items.DetachWorkItems();
            }
        }
        await NotifyAsync(item!, ExecutionPlanRealTimeEvents.WorkItemActions.Request, null, ct);
    }

    public async Task NotifyActivityAsync(Guid workItemId, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(workItemId, ct);
        if (item is not null)
            await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Activity, null, ct);
    }

    // ================================================================ apoio

    private async Task<WorkItem> MutateAsync(Func<Task<WorkItem?>> load, long number, Action<WorkItem> change, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var item = await load() ?? throw NotFound(number);
            change(item);
            try
            {
                await _items.SaveChangesAsync(ct);
                return item;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < ConcurrencyRetries)
            {
                _items.DetachWorkItems();
            }
        }
    }

    /// <summary>Tipo padrão: o do plano do card (analisar-us = User Story), senão o dos itens do card, senão bug.</summary>
    private async Task<string> DefaultTypeAsync(string cardNumber, CancellationToken ct)
    {
        var plans = await _plans.GetSummariesByCardAsync(cardNumber, ct);
        if (plans.OrderByDescending(p => p.CreatedAt).FirstOrDefault() is { } last)
            return string.Equals(last.Kind, UserStoryPlanKind, StringComparison.OrdinalIgnoreCase) ? WorkItemType.UserStory : WorkItemType.Bug;
        var items = await _items.ListByCardAsync(cardNumber, ct);
        return items.LastOrDefault()?.Type ?? WorkItemType.Bug;
    }

    private async Task<List<WorkItemResponse>> ToResponsesAsync(IReadOnlyList<WorkItem> items, CancellationToken ct)
    {
        var planIds = items.Where(i => i.PlanId is not null).Select(i => i.PlanId!.Value).Distinct().ToList();
        var requestIds = items.Where(i => i.ActiveRequestId is not null).Select(i => i.ActiveRequestId!.Value).Distinct().ToList();
        var phases = await _items.GetPlanPhasesAsync(planIds, ct);
        var activities = await _items.GetActivitiesAsync(requestIds, ct);
        return items.Select(i => WorkItemResponse.From(i,
            i.PlanId is { } p && phases.TryGetValue(p, out var phase) ? phase : null,
            i.ActiveRequestId is { } r && activities.TryGetValue(r, out var act) ? act : null)).ToList();
    }

    /// <summary>Timeline: criar, concluir, cancelar e desfazer.</summary>
    private async Task AfterStatusChangeAsync(WorkItem item, string before, ExecutionActor actor, CancellationToken ct)
    {
        if (item.Status == before) return;
        var text = item.Status switch
        {
            WorkItemStatus.Completed => $"Item de trabalho **#{item.Number}** concluído: {item.Title}.",
            WorkItemStatus.Cancelled => $"Item de trabalho **#{item.Number}** cancelado por {actor.Name}" +
                                        (item.CancelReason is null ? "." : $" — {item.CancelReason}"),
            WorkItemStatus.Reverted => $"Item de trabalho **#{item.Number}** desfeito: {item.Title}.",
            _ => null,
        };
        if (text is not null)
            await TimelineAsync(item, text, actor, ct);
    }

    private async Task TimelineAsync(WorkItem item, string markdown, ExecutionActor actor, CancellationToken ct) =>
        await TryAsync(() => _timeline.WriteAsync(item.CardNumber, markdown, actor.UserId, actor.Name, ct));

    private async Task NotifyAsync(WorkItem item, string action, long? taskNumber, CancellationToken ct) =>
        await TryAsync(() => _realTime.NotifyGroupAsync(
            ExecutionPlanRealTimeEvents.Group(item.CardNumber),
            ExecutionPlanRealTimeEvents.EventWorkItemsUpdated,
            new { cardNumber = item.CardNumber, number = item.Number, taskNumber, action, status = item.Status },
            ct));

    /// <summary>Tempo real e timeline são best-effort: nunca quebram a operação.</summary>
    private static async Task TryAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OperationCanceledException) { }
    }

    private static string? Serialize(JsonElement? output) =>
        output is { } o && o.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ? o.GetRawText() : null;

    private static string OutputSummary(JsonElement output)
    {
        if (output.ValueKind != JsonValueKind.Object) return string.Empty;
        var parts = output.EnumerateObject()
            .Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            .Select(p => $"{p.Name}={(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())}")
            .Take(8);
        return string.Join(", ", parts);
    }

    private static string OriginText(string origin) => origin switch
    {
        WorkItemOrigin.Specialist => "pelo especialista",
        WorkItemOrigin.Analysis => "pela análise",
        WorkItemOrigin.Correction => "pelo plano de correção",
        WorkItemOrigin.Template => "de um modelo",
        _ => "pelo usuário",
    };

    private static string NormalizeCard(string card)
    {
        var c = (card ?? string.Empty).Trim();
        if (c.Length == 0 || c.Length > 100)
            throw new DomainException("Informe o número do card.");
        return c;
    }

    private static ExecutionPlanNotFoundException NotFound(long number) => new($"Item de trabalho #{number} não encontrado.");
}
