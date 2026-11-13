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
    /// <summary>
    /// Card 77988: itens de trabalho a partir da análise já respondida (sem plano de entrega) — pede à fila a fase de
    /// implementação/correção; <paramref name="start"/> = false monta o plano e o deixa pausado até o Executar do item.
    /// </summary>
    Task<ExecutionRequestResponse> RequestDeliveryAsync(string cardNumber, bool start, string? note, ExecutionActor actor, CancellationToken cancellationToken);

    // Executor
    /// <summary>Pega um pedido para o executor, se houver — responde na hora (0068: sem long-poll).</summary>
    Task<ExecutionClaimResponse?> NextAsync(Guid workerId, CancellationToken cancellationToken);
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

    // 0080: Especialista do caso
    /// <summary>
    /// Pede uma resposta do especialista no card: na máquina do primeiro candidato que tem executor (quem escreveu, depois o
    /// dono do plano). Já há pedido na fila → ele lê a mensagem nova; rodando → roda de novo ao terminar.
    /// </summary>
    Task<ExecutionRequest?> EnqueueSpecialistAsync(string cardNumber, ExecutionActor requester, IReadOnlyList<(Guid UserId, string Name)> ownerCandidates,
        bool deep, CancellationToken cancellationToken);
    /// <summary>Pedido do especialista no card (o ativo, senão o último), com o motivo de espera.</summary>
    Task<ExecutionRequestResponse?> GetSpecialistRunAsync(string cardNumber, CancellationToken cancellationToken);
    /// <summary>Consumo do especialista no card desde <paramref name="since"/> (o último /clear; null = desde sempre).</summary>
    Task<CaseUsageResponse> GetSpecialistUsageAsync(string cardNumber, DateTimeOffset? since, CancellationToken cancellationToken);
    /// <summary>O token do caso ainda vale (o pedido do especialista deste card está ativo)?</summary>
    Task<bool> IsSpecialistRequestActiveAsync(Guid requestId, string cardNumber, CancellationToken cancellationToken);
    /// <summary>
    /// r5: o pedido do especialista deste card está ativo → quem pediu (a pessoa que escreveu na conversa) e o dono da máquina;
    /// null = o token do caso venceu. As ações que o especialista executa usam a identidade/integrações de quem pediu.
    /// </summary>
    Task<SpecialistRequester?> GetActiveSpecialistRequesterAsync(Guid requestId, string cardNumber, CancellationToken cancellationToken);

    // Configurações do usuário
    Task<ExecutionUserSettingsResponse> GetSettingsAsync(Guid userId, CancellationToken cancellationToken);
    Task<ExecutionUserSettingsResponse> UpdateSettingsAsync(Guid userId, UpdateExecutionUserSettingsRequest request, CancellationToken cancellationToken);

    /// <summary>Card 78344: plano do pedido ativo da pista principal do card (o que o executor foi chamado a retomar).</summary>
    Task<Guid?> ActivePlanIdAsync(string cardNumber, CancellationToken cancellationToken);
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

    /// <summary>0049: sessão do Claude que o executor está rodando agora no card (null = nenhuma).</summary>
    Task<ExecutionSession?> RunningSessionAsync(string cardNumber, CancellationToken cancellationToken);
}

/// <summary>0049: configurações da fila no PRMake ("Skills Configurations") — implementado no host.</summary>
public interface IExecutionQueueSettings
{
    Task<ExecutionQueueOptions> GetAsync(CancellationToken cancellationToken);
}

/// <param name="CorrectionInNewSession">A correção abre uma sessão nova do Claude em vez de retomar a da análise.</param>
/// <param name="WorkItemSlots">0085: itens de trabalho rodando ao mesmo tempo por máquina (fora do limite da pista principal).</param>
/// <remarks>0080: a espera depois de comentário (0049, <c>ExecutorNoteDelaySeconds</c>) saiu — o plano retoma na hora.</remarks>
public record ExecutionQueueOptions(bool CorrectionInNewSession, int WorkItemSlots = ExecutionQueueOptions.DefaultWorkItemSlots)
{
    public const int DefaultWorkItemSlots = 2;
    public static readonly ExecutionQueueOptions Default = new(true);
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

/// <summary>r5: quem pediu a resposta do especialista (null = sem pessoa: vale o dono) e o dono da máquina que responde.</summary>
public record SpecialistRequester(Guid? UserId, string Name, Guid OwnerUserId, string OwnerName);
