using solvace.azure.domain.Models;
using solvace.azure.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.azure.application.Contract;

/// <summary>
/// Menu "Ações DevOps" da tela do card (feature 0011). Regras não atendidas, card inexistente e
/// erros do DevOps lançam <see cref="solvace.azure.domain.Exceptions.DevOpsActionException"/>.
/// </summary>
public interface IDevOpsActionsService
{
    /// <summary>Valores efetivos do usuário (plugin "AI Configurations") para montar o menu.</summary>
    Task<DevOpsActionsConfigResponse> GetConfigAsync(CancellationToken cancellationToken = default);

    Task<DevOpsActionResponse> MoveToTestInProductionAsync(string cardNumber, CancellationToken cancellationToken = default);
    Task<DevOpsActionResponse> MoveToReadyForQaAsync(string cardNumber, CancellationToken cancellationToken = default);
    Task<DevOpsActionResponse> SetInitialEstimateAsync(string cardNumber, CancellationToken cancellationToken = default);
    Task<DevOpsActionResponse> ZeroRemainingAsync(string cardNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grava o resumo não técnico no registro do card e, com Publish, publica na discussion (cria ou
    /// atualiza o mesmo comentário). Sem registro salvo: DomainException.
    /// </summary>
    Task<PullRequestRegisterResponse> SaveSummaryAsync(string cardNumber, SaveSummaryRequest request, CancellationToken cancellationToken = default);
}
