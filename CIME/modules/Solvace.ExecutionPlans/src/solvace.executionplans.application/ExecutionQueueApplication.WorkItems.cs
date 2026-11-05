using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.application;

/// <summary>
/// 0085: pista dos itens de trabalho na fila — pedidos <see cref="ExecutionRequestKind.WorkItem"/> (executar as
/// sub-tarefas em ordem) e <see cref="ExecutionRequestKind.WorkItemUndo"/> (desfazer em ordem reversa). Um pedido ativo
/// por item (índice do banco), itens diferentes em paralelo, até <see cref="ExecutionQueueOptions.WorkItemSlots"/> por
/// máquina fora do limite da pista principal, só para executor ≥ <see cref="WorkItemAgentVersion"/>. Sessão nova sempre;
/// o modelo é o da correção. A fila avisa o item quando o pedido muda (queued/claimed/running/done/failed).
/// </summary>
public partial class ExecutionQueueApplication : IWorkItemQueue
{
    /// <summary>"Fase" gravada no pedido do item (o modelo vem de ExecutorCorrectionModel).</summary>
    public const string WorkItemPhase = "work-item";
    /// <summary>Versão do executor que conhece a pista dos itens de trabalho.</summary>
    public const string WorkItemAgentVersion = "1.3.0";

    public static bool SupportsWorkItems(ExecutionWorker worker) => !IsOlder(worker.AgentVersion, WorkItemAgentVersion);

    /// <summary>
    /// Itens de trabalho (resolvidos sob demanda: o <c>WorkItemApplication</c> depende desta fila — injetar direto fecharia
    /// um ciclo na DI).
    /// </summary>
    private IWorkItemApplication? WorkItemApp => _services?.GetService(typeof(IWorkItemApplication)) as IWorkItemApplication;

    private IPrmakeRulesProvider? Rules => _services?.GetService(typeof(IPrmakeRulesProvider)) as IPrmakeRulesProvider;

    public Task<Guid> EnqueueAsync(WorkItem item, ExecutionActor actor, CancellationToken ct) => EnqueueWorkItemAsync(item, false, actor, ct);

    public Task<Guid> EnqueueUndoAsync(WorkItem item, ExecutionActor actor, CancellationToken ct) => EnqueueWorkItemAsync(item, true, actor, ct);

    public async Task CancelActiveAsync(Guid workItemId, ExecutionActor actor, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (await _queue.GetActiveForWorkItemAsync(workItemId, ct) is not { } active)
                return;
            active.Cancel(actor.Name, $"Item de trabalho cancelado — {actor.Name}", now);
            try
            {
                await _queue.SaveChangesAsync(ct);
                // O executor vê no próximo sinal de vida (cancel) e encerra o processo.
                await NotifyWorkersAsync(active.OwnerUserId, ct);
                return;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < 5)
            {
                // O executor mexeu no pedido ao mesmo tempo (sinal de vida): relê só o pedido.
                await _queue.ReloadAsync(active, ct);
            }
        }
    }

    public async Task<string?> WaitCodeAsync(Guid requestId, CancellationToken ct)
    {
        var request = await _queue.GetRequestAsync(requestId, ct);
        if (request is null || request.Status != ExecutionRequestStatus.Queued) return null;
        var now = DateTimeOffset.UtcNow;
        return Diagnose(request, await WaitContextAsync(request.OwnerUserId, now, ct), now).Code;
    }

    /// <summary>Ajustes 0085: o pedido da pista principal mudou — os itens do plano avisam a sessão ao vivo (best-effort).</summary>
    private async Task NotifyPlanItemsRequestAsync(ExecutionRequest r, CancellationToken ct)
    {
        if (r.Kind == ExecutionRequestKind.Specialist || ExecutionRequestKind.IsWorkItem(r.Kind) || WorkItemApp is not { } items)
            return;
        try
        {
            if ((r.PlanId ?? await _plans.GetCurrentPlanIdAsync(r.CardNumber, ct)) is { } planId)
                await items.NotifyPlanRequestAsync(planId, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // best-effort
        }
    }

    private async Task<Guid> EnqueueWorkItemAsync(WorkItem item, bool undo, ExecutionActor actor, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var kind = undo ? ExecutionRequestKind.WorkItemUndo : ExecutionRequestKind.WorkItem;
        if (await _queue.GetActiveForWorkItemAsync(item.Id, ct) is { } active)
        {
            if (active.Kind == kind)
            {
                if (active.Status == ExecutionRequestStatus.Queued && active.RunNow(now))
                {
                    await SaveQuietlyAsync(ct);
                    await WakeWorkersAsync(active.OwnerUserId, ct);
                }
                return active.Id;
            }
            throw new DomainException(undo
                ? "O item ainda está executando — cancele antes de desfazer."
                : "O item está sendo desfeito — espere terminar.");
        }

        // Dono: quem pediu (se tiver executor); senão quem criou o item; senão quem pediu (o pedido espera uma máquina).
        var candidates = new List<(Guid UserId, string Name)>();
        if (actor.UserId is { } requester && requester != Guid.Empty) candidates.Add((requester, actor.Name));
        if (item.CreatedBy is { } creator && creator != Guid.Empty) candidates.Add((creator, item.CreatedByName));
        candidates = candidates.DistinctBy(c => c.UserId).ToList();
        if (candidates.Count == 0)
            throw new DomainException("Seu usuário não tem identificação no PRMake — entre de novo.");
        var withWorkers = await _queue.GetOwnersWithWorkersAsync(candidates.Select(c => c.UserId).ToList(), ct);
        var owner = candidates.FirstOrDefault(c => withWorkers.Contains(c.UserId));
        if (owner == default) owner = candidates[0];

        var source = actor.IsExecutor ? ExecutionRequestSource.Api : ExecutionRequestSource.Button;
        var request = ExecutionRequest.ForWorkItem(item, undo, source, owner.UserId, owner.Name, actor.UserId, actor.Name, null, null, now);
        var saved = await AddRequestAsync(request, ct) ?? request;
        await UpdateWaitReasonsAsync(owner.UserId, now, ct);
        return saved.Id;
    }

    /// <summary>Avisa o item que o pedido dele mudou (best-effort: o item se acerta no próximo aviso).</summary>
    private async Task AttachWorkItemAsync(ExecutionRequest r, CancellationToken ct)
    {
        if (!ExecutionRequestKind.IsWorkItem(r.Kind) || r.WorkItemId is not { } workItemId || WorkItemApp is not { } items)
            return;
        try
        {
            await items.AttachRequestAsync(workItemId, r.Id, r.Status, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // best-effort
        }
    }

    private async Task NotifyWorkItemActivityAsync(ExecutionRequest r, CancellationToken ct)
    {
        if (r.WorkItemId is not { } workItemId || WorkItemApp is not { } items) return;
        try
        {
            await items.NotifyActivityAsync(workItemId, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // best-effort
        }
    }

    /// <summary>Prompt do item: lê a SKILL.md (sem a ferramenta Skill — o modelo é o configurado) com o card e o item.</summary>
    private async Task<(string Prompt, long? Number)> WorkItemPromptAsync(ExecutionRequest r, CancellationToken ct)
    {
        var number = r.WorkItemId is { } id && WorkItemApp is { } items
            ? (await SafeGetItemAsync(items, id, ct))?.Number
            : null;
        var item = number is { } n ? $"item #{n}" : $"item {r.WorkItemId}";
        var skill = r.Kind == ExecutionRequestKind.WorkItemUndo ? "desfazer-item-de-trabalho" : "executar-item-de-trabalho";
        var what = r.Kind == ExecutionRequestKind.WorkItemUndo
            ? "DESFAZER o item de trabalho: as sub-tarefas concluidas em ordem reversa, cada uma pelo 'undo' dela (fechar PR, apagar branch criada, reverter commit, voltar o estado do DevOps)"
            : "EXECUTAR o item de trabalho: as sub-tarefas pendentes em ordem, registrando cada uma (start/complete/fail) com prmake_work_task";
        return ($"Card {r.CardNumber}, {item} — {what}. Leia as instrucoes com `cat ~/.claude/skills/{skill}/SKILL.md` (Bash) e siga-as — " +
               "NAO use a ferramenta Skill: o modelo desta execucao e o configurado no PRMake. " +
               $"Comece por prmake_work_item (card {r.CardNumber}, number {number?.ToString() ?? "?"}) e prmake_rules (card {r.CardNumber}): " +
               "nome de branch, base e destinos vem das regras — nunca invente. Toda gravacao passa pelo PRMake; nunca mescle PR." +
               ExecutorSuffix(r), number);
    }

    private static async Task<solvace.executionplans.domain.Responses.WorkItemResponse?> SafeGetItemAsync(IWorkItemApplication items, Guid id,
        CancellationToken ct)
    {
        try
        {
            return await items.GetByIdAsync(id, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// 0085: skill da análise do card — <c>analisar-us</c> numa User Story (pelo Kind do plano; sem plano, pelo tipo no
    /// DevOps), senão <c>analisar-bug</c>.
    /// </summary>
    private async Task<string> AnalysisSkillAsync(ExecutionRequest r, CancellationToken ct)
    {
        try
        {
            var planId = r.PlanId ?? await _plans.GetCurrentPlanIdAsync(r.CardNumber, ct);
            if (planId is { } id && await _plans.GetPlanWithStepsAsync(id, ct) is { } plan)
                return plan.Kind == UserStorySkill ? UserStorySkill : BugSkill;
            if (Rules is { } rules && await rules.IsUserStoryAsync(r.CardNumber, ct) == true)
                return UserStorySkill;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // sem o tipo: segue a skill de bug (a de sempre)
        }
        return BugSkill;
    }

    public const string BugSkill = "analisar-bug";
    public const string UserStorySkill = "analisar-us";
}
