using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.application.Contracts;

/// <summary>Fila de execução, executores e configurações do usuário (0039) — mesmo banco/contexto do plano.</summary>
public interface IExecutionQueueRepository
{
    void AddRequest(ExecutionRequest request);
    Task<ExecutionRequest?> GetRequestAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Pedido ativo (na fila, pego ou rodando) do card, rastreado.</summary>
    Task<ExecutionRequest?> GetActiveRequestForCardAsync(string cardNumber, CancellationToken cancellationToken);
    /// <summary>Pedidos ativos dos cards informados, sem rastrear.</summary>
    Task<List<ExecutionRequest>> GetActiveRequestsForCardsAsync(IReadOnlyCollection<string> cardNumbers, CancellationToken cancellationToken);
    Task<List<ExecutionRequest>> GetRecentRequestsForCardAsync(string cardNumber, int limit, CancellationToken cancellationToken);
    /// <summary>Pedidos na fila do dono, do mais antigo para o mais novo, rastreados.</summary>
    Task<List<ExecutionRequest>> GetQueuedForOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken);
    /// <summary>Pedidos pegos/rodando cuja trava venceu, e pedidos na fila há mais tempo que <paramref name="queuedBefore"/> — do dono, rastreados.</summary>
    Task<List<ExecutionRequest>> GetStaleForOwnerAsync(Guid ownerUserId, DateTimeOffset now, DateTimeOffset queuedBefore, CancellationToken cancellationToken);
    /// <summary>Pedidos pegos/rodando do executor (rastreados).</summary>
    Task<List<ExecutionRequest>> GetActiveForWorkerAsync(Guid workerId, CancellationToken cancellationToken);
    /// <summary>Quantos pedidos pegos/rodando cada executor do dono tem.</summary>
    Task<Dictionary<Guid, int>> CountActiveByWorkerAsync(Guid ownerUserId, CancellationToken cancellationToken);
    Task<decimal> GetCostSinceAsync(Guid ownerUserId, DateTimeOffset since, CancellationToken cancellationToken);
    /// <summary>0044: maior acumulado da sessão informado por OUTRO pedido já terminado (base do custo deste).</summary>
    Task<decimal?> GetLastSessionCostAsync(string sessionId, Guid exceptRequestId, CancellationToken cancellationToken);
    /// <summary>0049: o primeiro pedido que rodou nesta sessão do Claude (quem a começou) — null = sessão aberta fora do executor.</summary>
    Task<ExecutionRequest?> GetSessionStarterAsync(string sessionId, CancellationToken cancellationToken);
    Task<int> CountBySourceSinceAsync(Guid ownerUserId, string source, DateTimeOffset since, CancellationToken cancellationToken);
    /// <summary>Algum pedido do card (qualquer status) criado depois de <paramref name="since"/>?</summary>
    Task<bool> HasRequestForCardSinceAsync(string cardNumber, DateTimeOffset since, CancellationToken cancellationToken);
    /// <summary>Quando terminou o último pedido do card (null = nenhum).</summary>
    Task<DateTimeOffset?> GetLastFinishedAtAsync(string cardNumber, CancellationToken cancellationToken);
    Task<List<ExecutionRequest>> GetHistoryForOwnerAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken);

    void AddWorker(ExecutionWorker worker);
    Task<ExecutionWorker?> GetWorkerAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Executores do dono (revogados inclusos), rastreados.</summary>
    Task<List<ExecutionWorker>> GetWorkersByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken);
    Task<ExecutionWorker?> FindWorkerByHostAsync(Guid ownerUserId, string host, CancellationToken cancellationToken);
    /// <summary>Status e credencial válida do executor (sem rastrear) — para validar a credencial a cada request.</summary>
    Task<(string Status, Guid CredentialId)?> GetWorkerCredentialAsync(Guid workerId, CancellationToken cancellationToken);
    /// <summary>Dos usuários informados, quais têm algum executor não revogado.</summary>
    Task<HashSet<Guid>> GetOwnersWithWorkersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<ExecutionUserSettings?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken);
    void AddSettings(ExecutionUserSettings settings);

    /// <summary>Salva. Conflito de concorrência → <see cref="ExecutionPlanConcurrencyException"/>; pedido ativo duplicado no card → <see cref="ExecutionRequestDuplicateException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
    void ClearTracking();
}

/// <summary>Já existe um pedido ativo para o card (índice único parcial).</summary>
public class ExecutionRequestDuplicateException(Exception inner) : Exception("Já existe um pedido ativo para este card.", inner);
