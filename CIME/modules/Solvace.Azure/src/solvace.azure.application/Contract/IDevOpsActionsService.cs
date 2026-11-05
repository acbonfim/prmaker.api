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
    Task<DevOpsActionResponse> MoveToDevTestInQaAsync(string cardNumber, CancellationToken cancellationToken = default);
    Task<DevOpsActionResponse> SetInitialEstimateAsync(string cardNumber, CancellationToken cancellationToken = default);
    Task<DevOpsActionResponse> ZeroRemainingAsync(string cardNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// 0085: muda o estado do card para um estado válido do tipo dele (Bug ou User Story) — usado para desfazer a sub-tarefa
    /// <c>devops-state</c> de um item de trabalho (voltar ao estado anterior). Mesmo estado → 409.
    /// </summary>
    Task<DevOpsActionResponse> SetStateAsync(string cardNumber, string state, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// 0085 r3: estados válidos do tipo do card no DevOps, o atual e os próximos usuais (estados das ações configuradas
    /// Dev Test in QA / Ready for QA / Test in production que vêm depois do atual; sem elas, o próximo pela ordem e pela
    /// categoria). Card inexistente → 404; erro do DevOps → 502.
    /// </summary>
    Task<CardStatesResponse> GetCardStatesAsync(string cardNumber, CancellationToken cancellationToken = default);

    /// <summary>0078: horas do card (Original Estimate, Completed Work, Remaining Work).</summary>
    Task<DevOpsEffortResponse> GetEffortAsync(string cardNumber, CancellationToken cancellationToken = default);

    /// <summary>0078: lança horas trabalhadas — soma em Completed Work e tira do Remaining Work (sem ficar negativo).</summary>
    Task<DevOpsActionResponse> LogHoursAsync(string cardNumber, decimal hours, CancellationToken cancellationToken = default);

    /// <summary>Opções de classificação (padrão ou as do "AI Configurations") — feature 0028.</summary>
    Task<IReadOnlyList<DevOpsClassificationPreset>> GetClassificationPresetsAsync(CancellationToken cancellationToken = default);

    /// <summary>Grava Resolution Type · General Classification · Classification no card (feature 0028).</summary>
    Task<DevOpsActionResponse> ClassifyAsync(string cardNumber, ClassifyCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grava o resumo não técnico no registro do card e, com Publish, publica na discussion (cria ou
    /// atualiza o mesmo comentário). Sem registro salvo: DomainException.
    /// </summary>
    Task<PullRequestRegisterResponse> SaveSummaryAsync(string cardNumber, SaveSummaryRequest request, CancellationToken cancellationToken = default);
}
