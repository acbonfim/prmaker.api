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
    /// <summary>
    /// Leitura de <c>#n</c> citado (popover, MCP): o item com esse número ou, se for de uma sub-tarefa, o item dono com
    /// <see cref="WorkItemResponse.MatchedTaskNumber"/>. Para ações use <see cref="GetAsync"/> (só número de item).
    /// </summary>
    Task<WorkItemResponse> ResolveAsync(long number, bool withLogs, CancellationToken ct);
    /// <summary>
    /// Busca itens e sub-tarefas de qualquer card pelo começo do número ou por parte do título (ou pelo número do card),
    /// mais recentes primeiro — <paramref name="exceptCard"/> tira os do card informado.
    /// </summary>
    Task<IReadOnlyList<WorkItemSearchHit>> SearchAsync(string? query, string? exceptCard, int limit, CancellationToken ct);
    /// <param name="origin">user | specialist | analysis | correction | template (ver <see cref="WorkItemOrigin"/>).</param>
    Task<WorkItemResponse> CreateAsync(string card, CreateWorkItemRequest req, string origin, ExecutionActor actor, CancellationToken ct);
    Task<WorkItemResponse> UpdateAsync(long number, UpdateWorkItemRequest req, ExecutionActor actor, CancellationToken ct);
    Task<WorkItemResponse> DuplicateAsync(long number, ExecutionActor actor, CancellationToken ct);
    /// <summary>
    /// Reabre item avulso concluído/cancelado/com falha: as sub-tarefas de <see cref="ReopenWorkItemRequest.Redo"/> voltam a
    /// pendente; <see cref="ReopenWorkItemRequest.Execute"/> já põe na fila.
    /// </summary>
    Task<WorkItemResponse> ReopenAsync(long number, ReopenWorkItemRequest req, ExecutionActor actor, CancellationToken ct);
    /// <summary>Sub-tarefas a mais num item avulso aberto (antes de uma pendente ou no fim); script ganha as do chamado.</summary>
    Task<WorkItemResponse> AddTasksAsync(long number, AddWorkTasksRequest req, ExecutionActor actor, CancellationToken ct);
    /// <summary>Edita uma sub-tarefa pendente (item aberto, mesmo já começado). Campos null ficam.</summary>
    Task<WorkItemResponse> UpdateTaskAsync(long taskNumber, WorkTaskInput req, ExecutionActor actor, CancellationToken ct);
    /// <param name="action">start | complete | fail | wait | skip | revert | cancel (ver <see cref="WorkTaskAction"/>).</param>
    Task<WorkItemResponse> TaskActionAsync(long taskNumber, string action, WorkTaskActionRequest req, ExecutionActor actor, CancellationToken ct);
    /// <summary>"Já resolvi" na sub-tarefa que espera a pessoa: volta a andar e o item é retomado (item de plano: pelo plano).</summary>
    Task<WorkItemResponse> ResolveTaskAsync(long taskNumber, string? note, ExecutionActor actor, CancellationToken ct);
    /// <summary>Chamado na Bancada: anexa o chamado à sub-tarefa <c>ticket</c> (ela passa a aguardar o chamado).</summary>
    Task<WorkItemResponse> AttachTicketAsync(long taskNumber, WorkTaskTicketRequest req, ExecutionActor actor, CancellationToken ct);
    /// <summary>Chamado na Bancada: status do chamado da sub-tarefa (resolvido conclui; fechado volta para a pessoa).</summary>
    Task<WorkItemResponse> UpdateTicketAsync(long taskNumber, WorkTaskTicketStatusRequest req, ExecutionActor actor, CancellationToken ct);
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
    /// <param name="compact">Versão curta para o contexto do especialista (relido a cada resposta): sub-tarefas só dos itens em
    /// andamento, uma linha por item pendente/fechado e só os mais recentes — o resto vem contado (detalhe por prmake_work_item).</param>
    Task<string> SummaryMarkdownAsync(string card, CancellationToken ct, bool compact = false);

    // 0094: relatados do card (aba "Relatado", somente leitura; também entram no contexto da IA).
    /// <summary>Itens relatados ao card (menção/manual), resolvidos com sub-tarefas — mais recentes primeiro.</summary>
    Task<IReadOnlyList<WorkItemRelatedResponse>> ListRelatedAsync(string card, CancellationToken ct);
    /// <summary>Adiciona um relatado à mão (número de item ou sub-tarefa). 404 se não existe.</summary>
    Task<WorkItemRelatedResponse> AddRelatedManualAsync(string card, long number, ExecutionActor actor, CancellationToken ct);
    /// <summary>Remove um relatado (pelo número do item relatado). Idempotente.</summary>
    Task RemoveRelatedAsync(string card, long number, ExecutionActor actor, CancellationToken ct);
    /// <summary>
    /// Captura as menções (<c>#n</c>/<c>##n</c>) de um texto como relatados do card (upsert, só itens de **outro** card).
    /// Devolve quantos relatados foram criados/atualizados. Best-effort: não quebra a operação que o originou.
    /// </summary>
    Task<int> CaptureMentionsAsync(string card, string? text, string source, string? context, ExecutionActor actor, CancellationToken ct);
    /// <summary>Resumo compacto dos relatados do card para o contexto da IA (vazio quando não há). Detalhe fica sob demanda.</summary>
    Task<string> RelatedSummaryMarkdownAsync(string card, CancellationToken ct);
    /// <summary>O pedido de execução do item mudou (a fila chama ao criar/pegar/terminar o pedido).</summary>
    Task AttachRequestAsync(Guid workItemId, Guid requestId, string status, CancellationToken ct);

    // Extras
    /// <summary>Item por id (para a fila montar o prompt). null = não existe.</summary>
    Task<WorkItemResponse?> GetByIdAsync(Guid workItemId, CancellationToken ct);
    /// <summary>O executor relatou atividade no pedido do item — só repassa o sinal de tempo real.</summary>
    Task NotifyActivityAsync(Guid workItemId, CancellationToken ct);
    /// <summary>Atividade nova no pedido do plano — avisa os itens ligados a ele (workItemsUpdated, action activity).</summary>
    Task NotifyPlanActivityAsync(Guid planId, CancellationToken ct);
    /// <summary>
    /// Ajustes 0085: o pedido da pista principal do plano mudou (pego/começou/terminou) — avisa os itens abertos ligados ao
    /// plano (workItemsUpdated, action request).
    /// </summary>
    Task NotifyPlanRequestAsync(Guid planId, CancellationToken ct);
    /// <summary>
    /// Ajustes 0085: sessão de execução ao vivo do item (<c>GET WorkItems/{number}/live</c>) — pedido ativo do item (ou da
    /// pista principal do plano), atividade, últimas atividades, últimos 50 registros e se o executor tem sinal.
    /// </summary>
    Task<WorkItemLiveResponse> LiveAsync(long number, Guid? viewerUserId, CancellationToken ct);
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
    /// <summary>Ajustes 0085: motivo de espera (código) do pedido na fila — o mesmo da tela do plano; null fora da fila.</summary>
    Task<string?> WaitCodeAsync(Guid requestId, CancellationToken ct);
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
    /// <summary>Busca (projeção, sem texto grande) de itens e sub-tarefas — ver <see cref="IWorkItemApplication.SearchAsync"/>.</summary>
    Task<List<WorkItemSearchHit>> SearchAsync(string? query, string? exceptCard, int limit, CancellationToken cancellationToken);
    /// <summary>Itens ligados ao plano, rastreados.</summary>
    Task<List<WorkItem>> GetByPlanAsync(Guid planId, CancellationToken cancellationToken);
    /// <summary>Itens com esses números (com sub-tarefas, sem rastrear) — para montar os relatados do card (0094).</summary>
    Task<List<WorkItem>> ListByNumbersAsync(IReadOnlyCollection<long> numbers, CancellationToken cancellationToken);
    /// <summary>
    /// 0094: resolve um número citado (<c>#n</c>) para o item dono: item → o próprio; sub-tarefa → o item dono com o número
    /// da sub-tarefa; null = não existe. Projeção leve (sem carregar o item).
    /// </summary>
    Task<(long ItemNumber, string CardNumber, long? TaskNumber)?> ResolveNumberAsync(long number, CancellationToken cancellationToken);
    // 0094: relatados do card.
    void AddRelated(WorkItemRelated related);
    void RemoveRelated(WorkItemRelated related);
    Task<List<WorkItemRelated>> ListRelatedAsync(string cardNumber, CancellationToken cancellationToken);
    /// <summary>Relação existente (rastreada) de um item relatado a um card, ou null.</summary>
    Task<WorkItemRelated?> GetRelatedAsync(string cardNumber, long relatedItemNumber, CancellationToken cancellationToken);
    /// <summary>Item não cancelado/desfeito do card com o mesmo fingerprint (sem rastrear) — o item da análise não conta.</summary>
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
    /// <summary>Ajustes 0085: cabeçalho do item para o "ao vivo" (com status/dono/último sinal do plano), sem sub-tarefas.</summary>
    Task<WorkItemLiveHead?> GetLiveHeadAsync(long number, CancellationToken cancellationToken);
    /// <summary>
    /// Ajustes 0085: pedido ativo do item (work-item/work-item-undo) ou, sem ele e com <paramref name="planId"/>, o da pista
    /// principal do card ligado ao plano (ou ainda sem plano, quando o plano é o atual) — projetado, sem texto grande.
    /// </summary>
    Task<WorkItemLiveRequestRow?> GetLiveRequestAsync(Guid workItemId, string cardNumber, Guid? planId, CancellationToken cancellationToken);
    /// <summary>Ajustes 0085: card, número e status dos itens do plano (sem rastrear, sem sub-tarefas) — só para o tempo real.</summary>
    Task<List<(string CardNumber, long Number, string Status)>> GetPlanItemHeadsAsync(Guid planId, CancellationToken cancellationToken);
    /// <summary>Ajustes 0085: máquinas (não revogadas) do dono — id, status e último sinal.</summary>
    Task<List<WorkItemLiveWorker>> GetLiveWorkersAsync(Guid ownerUserId, CancellationToken cancellationToken);
    /// <summary>Fase dos planos informados (id → analysis|correction).</summary>
    Task<Dictionary<Guid, string>> GetPlanPhasesAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken);
    /// <summary>Salva. Conflito de concorrência → <see cref="ExecutionPlanConcurrencyException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
    /// <summary>Solta só os itens/sub-tarefas/registros rastreados (o plano e a fila no mesmo contexto ficam).</summary>
    void DetachWorkItems();
}
