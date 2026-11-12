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
    public const string UserStoryPlanKind = WorkItem.UserStoryPlanKind;

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
    private IWorkItemTypeSource? TypeSource => (IWorkItemTypeSource?)_services.GetService(typeof(IWorkItemTypeSource));
    private IPrmakeRulesProvider? Rules => (IPrmakeRulesProvider?)_services.GetService(typeof(IPrmakeRulesProvider));

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
            await AddDetailAsync(item, response, ct);
        return response;
    }

    /// <summary>Detalhe do GET por número: histórico e (0091) as entregas.</summary>
    private async Task AddDetailAsync(WorkItem item, WorkItemResponse response, CancellationToken ct)
    {
        response.Logs = (await _items.GetLogsAsync(item.Id, MaxLogs, ct)).Select(WorkItemLogResponse.From).ToList();
        if (_services.GetService(typeof(IWorkItemDeliverableApplication)) is IWorkItemDeliverableApplication deliverables)
            response.Deliverables = await deliverables.ListAsync(item, ct);
    }

    public async Task<WorkItemResponse> ResolveAsync(long number, bool withLogs, CancellationToken ct)
    {
        var item = await _items.GetByNumberAsync(number, ct);
        var matchedTask = false;
        if (item is null)
        {
            item = await _items.GetByTaskNumberAsync(number, ct) ?? throw NotFound(number);
            matchedTask = true;
        }
        var response = (await ToResponsesAsync([item], ct))[0];
        if (matchedTask)
            response.MatchedTaskNumber = number;
        if (withLogs)
            await AddDetailAsync(item, response, ct);
        return response;
    }

    public async Task<IReadOnlyList<WorkItemSearchHit>> SearchAsync(string? query, string? exceptCard, int limit, CancellationToken ct)
    {
        var except = string.IsNullOrWhiteSpace(exceptCard) ? null : NormalizeCard(exceptCard);
        return await _items.SearchAsync(query?.Trim().TrimStart('#'), except, Math.Clamp(limit, 1, 30), ct);
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

    // ================================================================ relatados (0094)

    /// <summary>Menção a item de trabalho num texto: <c>#n</c> ou <c>##n</c> (o número é o do item ou da sub-tarefa).</summary>
    private static readonly System.Text.RegularExpressions.Regex MentionRegex =
        new(@"#+(\d{1,9})", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<IReadOnlyList<WorkItemRelatedResponse>> ListRelatedAsync(string card, CancellationToken ct)
    {
        var cardNumber = NormalizeCard(card);
        var relations = await _items.ListRelatedAsync(cardNumber, ct);
        if (relations.Count == 0) return [];

        var numbers = relations.Select(r => r.RelatedItemNumber).Distinct().ToList();
        var items = await _items.ListByNumbersAsync(numbers, ct);
        var responses = (await ToResponsesAsync(items, ct)).ToDictionary(r => r.Number);

        var list = new List<WorkItemRelatedResponse>();
        foreach (var relation in relations) // já vem mais recente primeiro
            if (responses.TryGetValue(relation.RelatedItemNumber, out var item))
                list.Add(WorkItemRelatedResponse.From(relation, item));
        return list;
    }

    public async Task<WorkItemRelatedResponse> AddRelatedManualAsync(string card, long number, ExecutionActor actor, CancellationToken ct)
    {
        var cardNumber = NormalizeCard(card);
        var resolved = await _items.ResolveNumberAsync(number, ct) ?? throw NotFound(number);
        await UpsertRelatedAsync(cardNumber, resolved.ItemNumber, resolved.TaskNumber,
            WorkItemRelatedSource.Manual, context: null, actor, ct);
        await NotifyRelatedAsync(cardNumber, resolved.ItemNumber, ct);

        var relation = await _items.GetRelatedAsync(cardNumber, resolved.ItemNumber, ct) ?? throw NotFound(number);
        var items = await _items.ListByNumbersAsync([resolved.ItemNumber], ct);
        var item = (await ToResponsesAsync(items, ct)).FirstOrDefault() ?? throw NotFound(resolved.ItemNumber);
        return WorkItemRelatedResponse.From(relation, item);
    }

    public async Task RemoveRelatedAsync(string card, long number, ExecutionActor actor, CancellationToken ct)
    {
        var cardNumber = NormalizeCard(card);
        // O número removido é o do item relatado; se vier um número de sub-tarefa, resolve para o item dono.
        var relation = await _items.GetRelatedAsync(cardNumber, number, ct);
        if (relation is null && await _items.ResolveNumberAsync(number, ct) is { } resolved)
            relation = await _items.GetRelatedAsync(cardNumber, resolved.ItemNumber, ct);
        if (relation is null) return; // idempotente
        _items.RemoveRelated(relation);
        await _items.SaveChangesAsync(ct);
        await NotifyRelatedAsync(cardNumber, relation.RelatedItemNumber, ct);
    }

    public async Task<int> CaptureMentionsAsync(string card, string? text, string source, string? context, ExecutionActor actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var cardNumber = NormalizeCard(card);
        var numbers = MentionRegex.Matches(text)
            .Select(m => long.TryParse(m.Groups[1].Value, out var n) ? n : 0L)
            .Where(n => n > 0).Distinct().ToList();
        if (numbers.Count == 0) return 0;

        var changed = 0;
        foreach (var n in numbers)
        {
            var resolved = await _items.ResolveNumberAsync(n, ct);
            // Só item de OUTRO card vira relatado automático (o do próprio card já está na aba Itens).
            if (resolved is not { } r || string.Equals(r.CardNumber, cardNumber, StringComparison.Ordinal)) continue;
            if (await UpsertRelatedAsync(cardNumber, r.ItemNumber, r.TaskNumber, source, context, actor, ct, save: false))
                changed++;
        }
        if (changed == 0) return 0;
        await _items.SaveChangesAsync(ct);
        await NotifyRelatedAsync(cardNumber, null, ct);
        return changed;
    }

    /// <summary>Cria a relação ou reafirma a existente (manual nunca é rebaixado por auto). Devolve se criou/mudou.</summary>
    private async Task<bool> UpsertRelatedAsync(string cardNumber, long relatedItemNumber, long? relatedTaskNumber, string source,
        string? context, ExecutionActor actor, CancellationToken ct, bool save = true)
    {
        var existing = await _items.GetRelatedAsync(cardNumber, relatedItemNumber, ct);
        bool changed;
        if (existing is null)
        {
            _items.AddRelated(new WorkItemRelated(cardNumber, relatedItemNumber, relatedTaskNumber, source, context,
                actor.UserId, actor.Name, DateTimeOffset.UtcNow));
            changed = true;
        }
        else
        {
            changed = existing.Touch(source, context, relatedTaskNumber);
        }
        if (changed && save)
            await _items.SaveChangesAsync(ct);
        return changed;
    }

    public async Task<string> RelatedSummaryMarkdownAsync(string card, CancellationToken ct)
    {
        var relations = await ListRelatedAsync(card, ct);
        if (relations.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine($"## Relatados ({relations.Count})");
        sb.AppendLine("Itens de trabalho de outros cards relatados a este (menção ou manual). Para o detalhe, leia com `prmake_work_item(card, n)`.");
        foreach (var rel in relations)
        {
            var i = rel.Item;
            var progress = $"{i.Progress.Done}/{i.Progress.Total}";
            var where = SourceLabel(rel.Source);
            sb.AppendLine();
            sb.AppendLine($"- **#{i.Number}** (card {i.CardNumber}) [{i.Type}] {i.Title} — **{i.Status}** ({progress}) · relatado {where}" +
                          (rel.MatchedTaskNumber is { } mt ? $" · citada a sub-tarefa #{mt}" : string.Empty));
            foreach (var t in i.Tasks)
            {
                var line = $"  - #{t.Number} `{t.Kind}` {t.Title} — {t.Status}";
                if (!string.IsNullOrWhiteSpace(t.Result))
                    line += $" · {ShortLine(t.Result!, 160)}";
                sb.AppendLine(line);
            }
        }
        return sb.ToString();
    }

    private static string SourceLabel(string source) => source switch
    {
        WorkItemRelatedSource.Specialist => "por menção na conversa do especialista",
        WorkItemRelatedSource.Plan => "por menção no plano/análise",
        WorkItemRelatedSource.Timeline => "por menção na Timeline/comentário",
        WorkItemRelatedSource.ItemRef => "por menção num item do card",
        _ => "manualmente",
    };

    private static string ShortLine(string s, int max)
    {
        var t = s.Trim().ReplaceLineEndings(" ");
        return t.Length <= max ? t : t[..max] + "…";
    }

    private Task NotifyRelatedAsync(string cardNumber, long? number, CancellationToken ct) =>
        NotifyAsync(cardNumber, number ?? 0, WorkItemStatus.Pending, ExecutionPlanRealTimeEvents.WorkItemActions.Related, null, ct);

    /// <summary>Captura best-effort (nunca quebra a operação que a originou).</summary>
    private Task CaptureAsync(string card, string? text, string source, string? context, ExecutionActor actor, CancellationToken ct) =>
        TryAsync(() => CaptureMentionsAsync(card, text, source, context, actor, ct));

    /// <summary>Captura as menções de um item (descrição + sub-tarefas) como relatados (source item-ref).</summary>
    private Task CaptureItemRefAsync(string card, long itemNumber, string? description, IEnumerable<string?>? taskTexts,
        ExecutionActor actor, CancellationToken ct)
    {
        var text = string.Join('\n', new[] { description }.Concat(taskTexts ?? []).Where(s => !string.IsNullOrWhiteSpace(s)));
        return CaptureAsync(card, text, WorkItemRelatedSource.ItemRef, $"item #{itemNumber}", actor, ct);
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

        var settings = TypeSource is { } source ? await source.GetAsync(ct) : null;
        var type = ResolveType(req.Type, req.TemplateKey, settings);
        // r5: item com script ganha as sub-tarefas do chamado (gerar o arquivo + abrir no sistema de chamados).
        var tasks = WorkItemTicketRule.Apply(req.Tasks ?? [], settings?.TicketSystem);
        var now = DateTimeOffset.UtcNow;
        var item = new WorkItem(cardNumber, type, req.Title, req.Description, o, req.TemplateKey, planId, tasks,
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
        await CaptureItemRefAsync(cardNumber, item.Number, req.Description, (req.Tasks ?? []).Select(t => t.Description), actor, ct);
        return (await ToResponsesAsync([item], ct))[0];
    }

    public async Task<WorkItemResponse> UpdateAsync(long number, UpdateWorkItemRequest req, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var item = await MutateAsync(() => _items.GetByNumberAsync(number, ct), number,
            i => i.Update(req.Title ?? i.Title, req.Description, req.Tasks, actor.Name, DateTimeOffset.UtcNow), ct);
        await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Updated, null, ct);
        await CaptureItemRefAsync(item.CardNumber, item.Number, req.Description, (req.Tasks ?? []).Select(t => t.Description), actor, ct);
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
        if (a == WorkTaskAction.Complete && task.Kind == WorkTaskKind.CardBrief && !string.IsNullOrWhiteSpace(req.Result))
            await EnsureCardBriefFileAsync(item, taskNumber, req.Result, actor, ct);
        // 0094: o resultado de uma sub-tarefa pode citar itens de outros cards → relatados do card.
        if (!string.IsNullOrWhiteSpace(req.Result))
            await CaptureAsync(item.CardNumber, req.Result, WorkItemRelatedSource.ItemRef, $"sub-tarefa #{taskNumber}", actor, ct);
        return (await ToResponsesAsync([item], ct))[0];
    }

    /// <summary>
    /// "Já resolvi": a sub-tarefa do Claude parou esperando a pessoa (sem acesso ao banco, falta credencial, decisão...) e
    /// ela resolveu. Item avulso → a sub-tarefa volta a pendente com a nota e o item entra na fila de novo; sub-tarefa
    /// espelho → o "Já resolvi" da etapa do plano (a etapa volta a andar e o plano é retomado).
    /// </summary>
    public async Task<WorkItemResponse> ResolveTaskAsync(long taskNumber, string? note, ExecutionActor actor, CancellationToken ct)
    {
        var item = await _items.GetByTaskNumberAsync(taskNumber, ct)
                   ?? throw new ExecutionPlanNotFoundException($"Sub-tarefa #{taskNumber} não encontrada.");
        var task = item.Tasks.First(t => t.Number == taskNumber);
        if (item.PlanId is { } planId && task.StepKey is { } stepKey)
        {
            await PlanApp.ResolveStepAsync(planId, stepKey, note, actor, ct);
            _items.DetachWorkItems();
            return await GetAsync(item.Number, false, ct);
        }

        item = await MutateAsync(() => _items.GetByTaskNumberAsync(taskNumber, ct), item.Number,
            i => i.ResolveWaitingTask(taskNumber, note, actor.Name, DateTimeOffset.UtcNow), ct);
        await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Task, taskNumber, ct);
        return await ExecuteAsync(item.Number, actor, ct);
    }

    /// <summary>
    /// Chamado na Bancada: a pessoa anexa o chamado à sub-tarefa <c>ticket</c>. Sub-tarefa espelho → link bloqueante da etapa
    /// do plano (a etapa aguarda o chamado e conclui quando ele for resolvido); item avulso → chamado nas saídas da
    /// sub-tarefa, que passa a aguardar.
    /// </summary>
    public async Task<WorkItemResponse> AttachTicketAsync(long taskNumber, WorkTaskTicketRequest req, ExecutionActor actor, CancellationToken ct)
    {
        req ??= new WorkTaskTicketRequest();
        var url = WorkTaskTicket.NormalizeUrl(req.Url);
        var title = WorkTaskTicket.NormalizeTitle(req.Title);
        var (item, task) = await TicketTaskAsync(taskNumber, ct);
        if (item.PlanId is { } planId && task.StepKey is { } stepKey)
        {
            await PlanApp.AddLinkAsync(planId, stepKey, new AddExecutionLinkRequest
            {
                Url = url, Title = title, Kind = ExecutionLinkKind.Ticket, BlocksStep = true,
            }, actor, ct);
            _items.DetachWorkItems();
            return await GetAsync(item.Number, false, ct);
        }
        var output = WorkTaskTicket.Output(url, title, ExecutionLinkStatus.Open);
        var (action, result) = WorkTaskTicket.TaskAction(ExecutionLinkStatus.Open, WorkTaskTicket.DisplayName(url, title));
        return await TaskActionAsync(taskNumber, action, new WorkTaskActionRequest { Result = result, Output = JsonDocument.Parse(output).RootElement.Clone() }, actor, ct);
    }

    /// <summary>
    /// Chamado na Bancada: muda o status do chamado da sub-tarefa (open | resolved | closed). Resolvido conclui a
    /// sub-tarefa; fechado sem resolução volta para a pessoa. Sem <c>linkId</c>, vale o chamado mais recente.
    /// </summary>
    public async Task<WorkItemResponse> UpdateTicketAsync(long taskNumber, WorkTaskTicketStatusRequest req, ExecutionActor actor, CancellationToken ct)
    {
        req ??= new WorkTaskTicketStatusRequest();
        var status = WorkTaskTicket.NormalizeStatus(req.Status);
        var (item, task) = await TicketTaskAsync(taskNumber, ct);
        if (item.PlanId is { } planId && task.StepKey is { } stepKey)
        {
            var key = ExecutionStep.NormalizeKey(stepKey);
            var tickets = (await _plans.GetStepLinksAsync([planId], ct))
                .Where(l => l.StepKey == key && l.Kind == ExecutionLinkKind.Ticket).ToList();
            var link = (req.LinkId is { } id ? tickets.FirstOrDefault(l => l.Id == id) : tickets.LastOrDefault())
                       ?? throw new DomainException($"A sub-tarefa #{taskNumber} não tem chamado anexado — anexe o chamado antes.");
            await PlanApp.UpdateLinkAsync(planId, link.Id, new UpdateExecutionLinkRequest { Status = status }, actor, ct);
            _items.DetachWorkItems();
            return await GetAsync(item.Number, false, ct);
        }
        var current = WorkTaskTicket.Read(task)
                      ?? throw new DomainException($"A sub-tarefa #{taskNumber} não tem chamado anexado — anexe o chamado antes.");
        var output = WorkTaskTicket.Output(current.Url, current.Title, status);
        var (action, result) = WorkTaskTicket.TaskAction(status, WorkTaskTicket.DisplayName(current.Url, current.Title));
        return await TaskActionAsync(taskNumber, action, new WorkTaskActionRequest { Result = result, Output = JsonDocument.Parse(output).RootElement.Clone() }, actor, ct);
    }

    private async Task<(WorkItem Item, WorkItemTask Task)> TicketTaskAsync(long taskNumber, CancellationToken ct)
    {
        var item = await _items.GetByTaskNumberAsync(taskNumber, ct)
                   ?? throw new ExecutionPlanNotFoundException($"Sub-tarefa #{taskNumber} não encontrada.");
        var task = item.Tasks.First(t => t.Number == taskNumber);
        if (task.Kind != WorkTaskKind.Ticket)
            throw new DomainException($"A sub-tarefa #{taskNumber} não é de chamado.");
        if (WorkItemStatus.IsClosed(item.Status) || item.Status == WorkItemStatus.Reverting)
            throw new DomainException($"O item #{item.Number} está {item.Status} — o chamado não pode mais mudar.");
        if (WorkTaskStatus.IsDone(task.Status) || task.Status is WorkTaskStatus.Cancelled or WorkTaskStatus.Reverted)
            throw new DomainException($"A sub-tarefa #{taskNumber} já terminou ({task.Status}).");
        return (item, task);
    }

    /// <summary>
    /// 0089: o racional do pedido sempre vira arquivo do item (e, por isso, dos arquivos do card) — se o Claude concluiu o
    /// <c>card-brief</c> sem gravar <c>resumo-do-pedido.md</c>, o resultado é gravado com esse nome. Best-effort.
    /// </summary>
    private async Task EnsureCardBriefFileAsync(WorkItem item, long taskNumber, string result, ExecutionActor actor, CancellationToken ct)
    {
        if (_services.GetService(typeof(IWorkItemFileApplication)) is not IWorkItemFileApplication files)
            return;
        try
        {
            var existing = await files.ListAsync(item.Number, ct);
            if (existing.Any(f => f.Source == WorkItemFileResponse.SourceItem && string.Equals(f.Name, WorkTaskKind.CardBriefFileName, StringComparison.OrdinalIgnoreCase)))
                return;
            await files.UploadAsync(item.Number, new WorkItemFileUpload(WorkTaskKind.CardBriefFileName, WorkItemFileKind.Analysis, taskNumber,
                "text/markdown; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(result.Trim())), actor, ct);
        }
        catch (Exception e) when (e is DomainException or ExecutionPlanNotFoundException or WorkItemFileNumberConflictException) { }
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
                // A sub-tarefa espera alguém fazer algo (o motivo diz o quê): no plano é pendência da pessoa — ganha o "Já resolvi".
                await plans.UpdateStepAsync(planId, stepKey, new UpdateExecutionStepRequest
                {
                    Status = ExecutionStatus.Waiting,
                    Reason = result,
                    WaitingOn = string.IsNullOrWhiteSpace(result) ? ExecutionWaitingOn.External : ExecutionWaitingOn.User,
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
        var analysis = plan.Phase != ExecutionPhase.Correction;
        // r4: análise → analysis; correção → implementation (card User Story) ou fix.
        var type = analysis ? WorkItemType.Analysis : WorkItemType.CorrectionFor(await IsUserStoryCardAsync(cardNumber, plan.Kind, ct));
        var origin = analysis ? WorkItemOrigin.Analysis : WorkItemOrigin.Correction;
        var title = WorkItem.PlanItemTitle(analysis ? ExecutionPhase.Analysis : ExecutionPhase.Correction, plan.Kind, cardNumber, plan.Title);
        var description = plan.Summary is { Length: > WorkItem.MaxDescriptionLength } s ? s[..WorkItem.MaxDescriptionLength] : plan.Summary;
        var item = new WorkItem(cardNumber, type, title, description, origin, null, plan.Id, tasks,
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
        await TimelineAsync(item, $"Item de trabalho **#{item.Number}** criado do plano de {(plan.Phase == ExecutionPhase.Correction ? PlanTerms.Delivery(plan.Kind) : "análise")}: {item.Title} — {item.Tasks.Count} sub-tarefa(s).", actor, ct);
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
        {
            if (IsMirror(item))
                item.SyncStepKind(step.Key, StepTaskKind(step), ServerKinds(step), now);
            item.SyncStep(step.Key, step.Status, step.StatusReason, actor, now);
        }
    }

    private static string StepTaskKind(ExecutionStep s) => WorkTaskKind.FromStep(s.Kind, s.Executor, s.Key, s.Title, s.TaskKind);

    /// <summary>Tipos que o servidor já deu a esta etapa (antes da dedução e sem o taskKind da skill).</summary>
    private static string[] ServerKinds(ExecutionStep s) =>
        [WorkTaskKind.LegacyFromStep(s.Kind, s.Executor), WorkTaskKind.FromStep(s.Kind, s.Executor, s.Key, s.Title)];

    /// <summary>Item que espelha as etapas do plano (gerado do plano; a análise pode nascer sem etapas).</summary>
    private static bool IsMirror(WorkItem item) => item.MirrorsPlan;

    private static IEnumerable<WorkTaskInput> StepInputs(IEnumerable<ExecutionStep> steps) =>
        steps.OrderBy(s => s.Order).Select(s => new WorkTaskInput
        {
            Title = s.Title,
            Description = s.Description,
            Kind = StepTaskKind(s),
            Executor = s.Executor,
            Params = string.IsNullOrWhiteSpace(s.Repository) ? null : new Dictionary<string, string> { ["repository"] = s.Repository! },
            StepKey = s.Key,
        });

    // ================================================================ fila

    public async Task AttachRequestAsync(Guid workItemId, Guid requestId, string status, CancellationToken ct)
    {
        WorkItem? item = null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                item = await _items.GetByIdAsync(workItemId, ct);
                if (item is null) return;
                // Ajustes 0085: o sinal vai mesmo sem mudança no item (pegou/começou/terminou o pedido) — a sessão ao vivo
                // refaz o GET {number}/live.
                if (!item.AttachRequest(requestId, status, DateTimeOffset.UtcNow)) break;
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

    public Task NotifyPlanActivityAsync(Guid planId, CancellationToken ct) =>
        NotifyPlanItemsAsync(planId, ExecutionPlanRealTimeEvents.WorkItemActions.Activity, ct);

    public Task NotifyPlanRequestAsync(Guid planId, CancellationToken ct) =>
        NotifyPlanItemsAsync(planId, ExecutionPlanRealTimeEvents.WorkItemActions.Request, ct);

    /// <summary>Só o sinal (sem rastrear os itens: quem chama pode estar no meio de outra gravação no mesmo contexto).</summary>
    private async Task NotifyPlanItemsAsync(Guid planId, string action, CancellationToken ct)
    {
        foreach (var (card, number, status) in await _items.GetPlanItemHeadsAsync(planId, ct))
            if (!WorkItemStatus.IsClosed(status))
                await NotifyAsync(card, number, status, action, null, ct);
    }

    // ================================================================ ao vivo

    public async Task<WorkItemLiveResponse> LiveAsync(long number, Guid? viewerUserId, CancellationToken ct)
    {
        var head = await _items.GetLiveHeadAsync(number, ct) ?? throw NotFound(number);
        // Item fechado não mostra o pedido do plano (que já é de outra rodada); o pedido do próprio item (desfazer) vale.
        var planId = WorkItemStatus.IsClosed(head.Status) ? null : head.PlanId;
        var request = await _items.GetLiveRequestAsync(head.Id, head.CardNumber, planId, ct);
        string? waitCode = null;
        if (request?.Status == ExecutionRequestStatus.Queued && _services.GetService(typeof(IWorkItemQueue)) is IWorkItemQueue queue)
        {
            try { waitCode = await queue.WaitCodeAsync(request.Id, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { /* sem o motivo: a tela mostra "na fila" */ }
        }
        var logs = await _items.GetLogsAsync(head.Id, WorkItemLive.MaxLogs, ct);
        var owner = WorkItemLive.OwnerOf(head, request, viewerUserId);
        var workers = owner is { } o ? await _items.GetLiveWorkersAsync(o, ct) : [];
        return WorkItemLive.Build(head, request, waitCode, logs, workers, DateTimeOffset.UtcNow);
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

    /// <summary>
    /// r4: tipo do item novo — o pedido (bug/user-story legados → fix/implementation), senão o do modelo, senão task; precisa
    /// existir no <c>WorkItemTypes</c> (400 com as opções).
    /// </summary>
    /// <summary>Regra do tipo do item novo (testável): pedido → modelo → task; legado mapeado; conferido contra os tipos.</summary>
    public static string ResolveType(string? requested, string? templateKey, WorkItemTypeSettings? settings)
    {
        var type = WorkItemType.MapLegacy(requested);
        if (type is null && !string.IsNullOrWhiteSpace(templateKey) && settings is not null
            && settings.TemplateTypes.TryGetValue(templateKey.Trim(), out var fromTemplate))
            type = WorkItemType.MapLegacy(fromTemplate);
        type = WorkItemType.Normalize(type ?? WorkItemType.Task);
        if (settings is not null)
        {
            var keys = settings.Keys.Count > 0 ? settings.Keys : WorkItemType.Defaults;
            if (!keys.Contains(type, StringComparer.OrdinalIgnoreCase))
                throw new WorkItemTypeInvalidException(type, keys);
        }
        return type;
    }

    /// <summary>Card User Story? Pelo Kind do plano (analisar-us), senão pelo card no DevOps (regras do PRMake).</summary>
    private async Task<bool> IsUserStoryCardAsync(string cardNumber, string? planKind, CancellationToken ct)
    {
        if (WorkItem.IsUserStoryPlan(planKind)) return true;
        var plans = await _plans.GetSummariesByCardAsync(cardNumber, ct);
        if (plans.Any(p => WorkItem.IsUserStoryPlan(p.Kind))) return true;
        return Rules is { } rules && await rules.IsUserStoryAsync(cardNumber, ct) == true;
    }

    private async Task<List<WorkItemResponse>> ToResponsesAsync(IReadOnlyList<WorkItem> items, CancellationToken ct)
    {
        var planIds = items.Where(i => i.PlanId is not null).Select(i => i.PlanId!.Value).Distinct().ToList();
        var phases = await _items.GetPlanPhasesAsync(planIds, ct);
        // Pedido ativo e atividade em lote: o do próprio item (pista work-item) ou, no item de plano, o do plano.
        var openItems = items.Where(i => i.Status is not (WorkItemStatus.Completed or WorkItemStatus.Cancelled or WorkItemStatus.Reverted)).ToList();
        var (byItem, byPlan) = await _items.GetLiveRequestsAsync(openItems.Select(i => i.Id).ToList(),
            openItems.Where(i => i.PlanId is not null).Select(i => i.PlanId!.Value).Distinct().ToList(), ct);
        var stepLinks = (await _plans.GetStepLinksAsync(planIds, ct)).ToLookup(l => (l.PlanId, l.StepKey));
        return items.Select(i =>
        {
            (Guid RequestId, string? Activity)? live = byItem.TryGetValue(i.Id, out var own) ? own
                : i.PlanId is { } pid && byPlan.TryGetValue(pid, out var plan) ? plan : null;
            var response = WorkItemResponse.From(i, i.PlanId is { } p && phases.TryGetValue(p, out var phase) ? phase : null, live?.Activity);
            if (live is { } l) response.RequestId = l.RequestId;
            if (i.PlanId is { } planId)
                foreach (var task in response.Tasks.Where(t => t.StepKey is not null))
                    task.Links.AddRange(stepLinks[(planId, task.StepKey!)].Select(WorkTaskLinkResponse.From));
            return response;
        }).ToList();
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

    private Task NotifyAsync(WorkItem item, string action, long? taskNumber, CancellationToken ct) =>
        NotifyAsync(item.CardNumber, item.Number, item.Status, action, taskNumber, ct);

    private async Task NotifyAsync(string cardNumber, long number, string status, string action, long? taskNumber, CancellationToken ct) =>
        await TryAsync(() => _realTime.NotifyGroupAsync(
            ExecutionPlanRealTimeEvents.Group(cardNumber),
            ExecutionPlanRealTimeEvents.EventWorkItemsUpdated,
            new { cardNumber, number, taskNumber, action, status },
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
