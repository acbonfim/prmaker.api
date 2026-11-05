using System.Security.Claims;
using solvace.azure.application.Contract;
using solvace.azure.domain.Models;
using solvace.prform.Controllers;
using solvace.timeline.application.Contracts;

namespace solvace.prform.Execution;

/// <summary>
/// Ação do DevOps com o mesmo efeito do endpoint (0041): executa e registra na Timeline do card como "… pelo CIME
/// (Ações DevOps)". O MCP usa este caminho; erro do DevOps sobe como <c>DevOpsActionException</c>.
/// </summary>
public class DevOpsActionRunner(IDevOpsActionsService actions, ITimelineApplication timeline, ILogger<DevOpsActionRunner> logger)
{
    /// <param name="via">r5: marca de quem fez (ex.: "via Especialista do caso, a pedido de Ana") no texto da Timeline.</param>
    public async Task<DevOpsActionResponse> RunAsync(string cardNumber, Func<IDevOpsActionsService, Task<DevOpsActionResponse>> action,
        ClaimsPrincipal user, CancellationToken cancellationToken, string? via = null)
    {
        var result = await action(actions);
        var text = string.IsNullOrWhiteSpace(via)
            ? $"{result.Message} pelo CIME (Ações DevOps)."
            : $"{result.Message} pelo CIME (Ações DevOps) — {via}.";
        await AzureController.RegisterOnTimelineAsync(timeline, cardNumber, text, logger, user, cancellationToken);
        return result;
    }

    public Task<DevOpsActionsConfigResponse> ConfigAsync(CancellationToken cancellationToken) => actions.GetConfigAsync(cancellationToken);

    /// <summary>0078: horas do card (Original/Completed/Remaining).</summary>
    public Task<DevOpsEffortResponse> EffortAsync(string cardNumber, CancellationToken cancellationToken) => actions.GetEffortAsync(cardNumber, cancellationToken);

    public Task<IReadOnlyList<DevOpsClassificationPreset>> ClassificationsAsync(CancellationToken cancellationToken) =>
        actions.GetClassificationPresetsAsync(cancellationToken);
}
