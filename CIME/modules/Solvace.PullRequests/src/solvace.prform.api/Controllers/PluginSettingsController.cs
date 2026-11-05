using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.prform.PluginSettings;

namespace solvace.prform.Controllers;

/// <summary>
/// 0086: configurações por seção (não por plugin) — o catálogo das chaves resolvido contra os plugins, a gravação só das
/// chaves alteradas (com checagem do valor lido e da versão) e o histórico. As rotas antigas do
/// PluginConfigurationController continuam iguais.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/PluginConfiguration/settings")]
[Authorize(Roles = "admin")]
public class PluginSettingsController(PluginSettingsService service) : ControllerBase
{
    /// <summary>Seções → grupos → campos, plugins (com a versão), chaves fora do catálogo e plugins que faltam.</summary>
    [HttpGet]
    public async Task<ActionResult<SettingsResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(cancellationToken));

    /// <summary>
    /// Grava só as chaves enviadas (numa transação, podendo tocar vários plugins). 409 = alguém mudou o valor depois que a
    /// tela leu (nada gravado); 400 = valor inválido para o tipo.
    /// </summary>
    [HttpPatch]
    [ProducesResponseType<SettingsPatchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SettingsConflictResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<SettingsErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Patch([FromBody] SettingsPatchRequest request, CancellationToken cancellationToken)
    {
        var result = await service.PatchAsync(request, cancellationToken);
        if (result.Errors is { Count: > 0 } errors)
            return BadRequest(new SettingsErrorResponse { Errors = errors.ToList() });
        if (result.Conflicts is { Count: > 0 } conflicts)
            return Conflict(new SettingsConflictResponse { Conflicts = conflicts.ToList() });
        return Ok(result.Ok);
    }

    /// <summary>Histórico das alterações (mais recente primeiro; segredo mascarado).</summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<SettingHistoryResponse>>> History([FromQuery] int? pluginId, [FromQuery] string? key,
        [FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await service.HistoryAsync(pluginId, key, take, cancellationToken));
}
