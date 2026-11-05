using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application.Contracts;

/// <summary>
/// 0085: itens de trabalho do card (REST <c>api/v1/WorkItems</c>, MCP <c>prmake_work_*</c> e o plano de correção).
/// Erros: <see cref="WorkItemDuplicateException"/> (409 duplicate), <see cref="WorkItemNotEditableException"/>
/// (409 notEditable), <see cref="ExecutionPlanNotFoundException"/> (404), <see cref="DomainException"/> (400).
/// </summary>
public interface IWorkItemApplication
{
    /// <summary>Itens do card (com sub-tarefas), em ordem de número.</summary>
    Task<IReadOnlyList<WorkItemResponse>> ListAsync(string card, CancellationToken ct);
    Task<WorkItemResponse> GetAsync(long number, bool withLogs, CancellationToken ct);
    /// <param name="origin">user | specialist | analysis | correction | template (ver <see cref="WorkItemOrigin"/>).</param>
    Task<WorkItemResponse> CreateAsync(string card, CreateWorkItemRequest req, string origin, ExecutionActor actor, CancellationToken ct);
    Task<WorkItemResponse> UpdateAsync(long number, UpdateWorkItemRequest req, ExecutionActor actor, CancellationToken ct);
    Task<WorkItemResponse> DuplicateAsync(long number, ExecutionActor actor, CancellationToken ct);
    /// <param name="action">start | complete | fail | wait | skip | revert | cancel (ver <see cref="WorkTaskAction"/>).</param>
    Task<WorkItemResponse> TaskActionAsync(long taskNumber, string action, WorkTaskActionRequest req, ExecutionActor actor, CancellationToken ct);
    /// <summary>Plano ligado → retoma o plano (fila main); avulso → <see cref="IWorkItemQueue.EnqueueAsync"/>.</summary>
    Task<WorkItemResponse> ExecuteAsync(long number, ExecutionActor actor, CancellationToken ct);
    Task<WorkItemResponse> CancelAsync(long number, bool undo, string? reason, ExecutionActor actor, CancellationToken ct);
    /// <summary>Desfaz item já cancelado/concluído/falho (pedido <c>work-item-undo</c>).</summary>
    Task<WorkItemResponse> UndoAsync(long number, ExecutionActor actor, CancellationToken ct);
    /// <summary>Item do plano (uma sub-tarefa por etapa) — idempotente: devolve o existente.</summary>
    Task<WorkItemResponse> FromPlanAsync(string card, Guid planId, ExecutionActor actor, CancellationToken ct);
    /// <summary>Etapas → sub-tarefas (por StepKey); plano concluído → item completed; cancelado → cancelled.</summary>
    Task SyncFromPlanAsync(Guid planId, CancellationToken ct);
    Task<IReadOnlyList<WorkItemResponse>> CancelByPlanAsync(Guid planId, bool undo, string? reason, ExecutionActor actor, CancellationToken ct);
    /// <summary>Markdown dos itens do card para o contexto do especialista (número, tipo, título, status, origem, fingerprint, sub-tarefas e saídas).</summary>
    Task<string> SummaryMarkdownAsync(string card, CancellationToken ct);
    /// <summary>O pedido de execução do item mudou (a fila chama ao criar/pegar/terminar o pedido).</summary>
    Task AttachRequestAsync(Guid workItemId, Guid requestId, string status, CancellationToken ct);

    // Extras
    /// <summary>Item por id (para a fila montar o prompt). null = não existe.</summary>
    Task<WorkItemResponse?> GetByIdAsync(Guid workItemId, CancellationToken ct);
    /// <summary>O executor relatou atividade no pedido do item — só repassa o sinal de tempo real.</summary>
    Task NotifyActivityAsync(Guid workItemId, CancellationToken ct);
    /// <summary>Atividade nova no pedido do plano — avisa os itens ligados a ele (workItemsUpdated, action activity).</summary>
    Task NotifyPlanActivityAsync(Guid planId, CancellationToken ct);
}

/// <summary>
/// 0085: fila dos itens de trabalho — implementada pelo módulo da fila (B2). O item já vem salvo; a implementação cria o
/// <see cref="ExecutionRequest"/> (<see cref="ExecutionRequest.ForWorkItem"/>) e devolve o id. Pedido ativo do item
/// já existente → devolve o id dele.
/// </summary>
public interface IWorkItemQueue
{
    Task<Guid> EnqueueAsync(WorkItem item, ExecutionActor actor, CancellationToken ct);
    Task<Guid> EnqueueUndoAsync(WorkItem item, ExecutionActor actor, CancellationToken ct);
    Task CancelActiveAsync(Guid workItemId, ExecutionActor actor, CancellationToken ct);
}

/// <summary>0085: itens de trabalho — mesmo banco/contexto do plano.</summary>
public interface IWorkItemRepository
{
    void Add(WorkItem item);
    /// <summary>Itens do card com sub-tarefas (sem rastrear), em ordem de número.</summary>
    Task<List<WorkItem>> ListByCardAsync(string cardNumber, CancellationToken cancellationToken);
    /// <summary>Item com sub-tarefas, rastreado.</summary>
    Task<WorkItem?> GetByNumberAsync(long number, CancellationToken cancellationToken);
    Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Item dono da sub-tarefa, rastreado.</summary>
    Task<WorkItem?> GetByTaskNumberAsync(long taskNumber, CancellationToken cancellationToken);
    /// <summary>Itens ligados ao plano, rastreados.</summary>
    Task<List<WorkItem>> GetByPlanAsync(Guid planId, CancellationToken cancellationToken);
    /// <summary>Item não cancelado/desfeito do card com o mesmo fingerprint (sem rastrear).</summary>
    Task<WorkItem?> FindDuplicateAsync(string cardNumber, string fingerprint, Guid? exceptId, CancellationToken cancellationToken);
    Task<List<WorkItemLog>> GetLogsAsync(Guid workItemId, int limit, CancellationToken cancellationToken);
    /// <summary>
    /// Pedidos ativos em lote: do próprio item (por WorkItemId) e, para itens de plano, o pedido ativo da pista principal do
    /// plano (por PlanId) — id e atividade atual (sem N+1).
    /// </summary>
    Task<(Dictionary<Guid, (Guid RequestId, string? Activity)> ByItem, Dictionary<Guid, (Guid RequestId, string? Activity)> ByPlan)>
        GetLiveRequestsAsync(IReadOnlyCollection<Guid> workItemIds, IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken);
    /// <summary>Atividade atual dos pedidos informados (id → rótulo).</summary>
    Task<Dictionary<Guid, string>> GetActivitiesAsync(IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken);
    /// <summary>Fase dos planos informados (id → analysis|correction).</summary>
    Task<Dictionary<Guid, string>> GetPlanPhasesAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken);
    /// <summary>Salva. Conflito de concorrência → <see cref="ExecutionPlanConcurrencyException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
    /// <summary>Solta só os itens/sub-tarefas/registros rastreados (o plano e a fila no mesmo contexto ficam).</summary>
    void DetachWorkItems();
}
