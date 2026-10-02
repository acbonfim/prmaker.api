using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application.Contracts;

/// <summary>Fila de execução e executores (0039).</summary>
public interface IExecutionQueueApplication
{
    // Tela
    Task<ExecutionRequestResponse> CreateAsync(CreateExecutionRequestRequest request, ExecutionActor actor, string source, CancellationToken cancellationToken);
    Task<ExecutionCardQueueResponse> GetCardAsync(string cardNumber, Guid? viewerUserId, CancellationToken cancellationToken);
    Task<ExecutionRequestResponse> CancelAsync(Guid requestId, string? reason, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionRequestResponse> RetryAsync(Guid requestId, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionRequestResponse> AllowOverBudgetAsync(Guid requestId, ExecutionActor actor, CancellationToken cancellationToken);
    Task<List<ExecutionRequestResponse>> GetHistoryAsync(Guid ownerUserId, CancellationToken cancellationToken);

    // Executor
    /// <summary>Long-poll: devolve um pedido assim que existir (ou null depois de <paramref name="wait"/>).</summary>
    Task<ExecutionClaimResponse?> NextAsync(Guid workerId, TimeSpan wait, CancellationToken cancellationToken);
    Task<ExecutionRequestResponse> StartAsync(Guid requestId, Guid workerId, StartExecutionRequestRequest request, CancellationToken cancellationToken);
    Task<ExecutionHeartbeatResponse> HeartbeatAsync(Guid requestId, Guid workerId, ExecutionRequestHeartbeatRequest request, CancellationToken cancellationToken);
    Task<ExecutionRequestResponse> FinishAsync(Guid requestId, Guid workerId, FinishExecutionRequestRequest request, CancellationToken cancellationToken);
    /// <summary>
    /// 0044: plano onde gravar o consumo final da sessão — o atual do card quando a sessão está nele (a análise virou
    /// correção no meio da execução), senão o do pedido.
    /// </summary>
    Task<Guid?> ResolvePlanIdAsync(Guid requestId, string sessionId, CancellationToken cancellationToken);

    // Executores
    Task<(ExecutionWorker Worker, ExecutionWorkerResponse Response)> RegisterWorkerAsync(RegisterExecutionWorkerRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionWorkerStateResponse> ReportWorkerAsync(Guid workerId, ExecutionWorkerReportRequest request, CancellationToken cancellationToken);
    Task<ExecutionWorkerResponse> ReportDoctorAsync(Guid workerId, ExecutionWorkerDoctorRequest request, CancellationToken cancellationToken);
    Task<ExecutionWorkerResponse> GetWorkerAsync(Guid workerId, CancellationToken cancellationToken);
    Task<List<ExecutionWorkerResponse>> GetWorkersAsync(Guid ownerUserId, CancellationToken cancellationToken);
    Task<ExecutionWorkerResponse> ConfigureWorkerAsync(Guid workerId, ConfigureExecutionWorkerRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionWorkerResponse> RequestDoctorAsync(Guid workerId, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionWorkerResponse> PauseWorkerAsync(Guid workerId, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionWorkerResponse> ResumeWorkerAsync(Guid workerId, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionWorkerResponse> RevokeWorkerAsync(Guid workerId, ExecutionActor actor, CancellationToken cancellationToken);
    /// <summary>A credencial do executor ainda vale (não revogado, mesmo jti)?</summary>
    Task<bool> IsCredentialValidAsync(Guid workerId, Guid credentialId, CancellationToken cancellationToken);

    // Configurações do usuário
    Task<ExecutionUserSettingsResponse> GetSettingsAsync(Guid userId, CancellationToken cancellationToken);
    Task<ExecutionUserSettingsResponse> UpdateSettingsAsync(Guid userId, UpdateExecutionUserSettingsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// O plano mudou por uma ação de fora do Claude (resposta, etapa do usuário, chamado, PR mesclado, "Continuar",
/// comentário): se o dono tem executor e ninguém está rodando o card, nasce um pedido de <c>resume</c> (0039).
/// </summary>
public interface IExecutionResumeTrigger
{
    Task<ExecutionRequest?> PlanChangedAsync(ExecutionPlan plan, string source, ExecutionActor actor, bool explicitRequest, CancellationToken cancellationToken);

    /// <summary>Dos usuários informados, quais têm executor (o vigia antigo da 0033 ignora os planos deles).</summary>
    Task<HashSet<Guid>> OwnersWithWorkersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>Itens do Azure DevOps que casam com a regra automática do usuário (0039) — implementado no host (WIQL).</summary>
public interface IExecutionWorkItemSource
{
    Task<IReadOnlyList<string>> FindCardsAsync(ExecutionUserSettings settings, int max, CancellationToken cancellationToken);
}

/// <summary>Versão do executor publicada pelo PRMake (implementado no host).</summary>
public interface IExecutionAgentInfo
{
    string? LatestVersion { get; }
}

/// <summary>Ação de quem não é dono (ex.: pausar o executor de outra pessoa).</summary>
public class ExecutionForbiddenException(string message) : Exception(message);
