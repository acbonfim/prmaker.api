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
    public async Task<DevOpsActionResponse> RunAsync(string cardNumber, Func<IDevOpsActionsService, Task<DevOpsActionResponse>> action,
        ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var result = await action(actions);
        await AzureController.RegisterOnTimelineAsync(timeline, cardNumber, $"{result.Message} pelo CIME (Ações DevOps).", logger, user, cancellationToken);
        return result;
    }

    public Task<DevOpsActionsConfigResponse> ConfigAsync(CancellationToken cancellationToken) => actions.GetConfigAsync(cancellationToken);

    public Task<IReadOnlyList<DevOpsClassificationPreset>> ClassificationsAsync(CancellationToken cancellationToken) =>
        actions.GetClassificationPresetsAsync(cancellationToken);
}
