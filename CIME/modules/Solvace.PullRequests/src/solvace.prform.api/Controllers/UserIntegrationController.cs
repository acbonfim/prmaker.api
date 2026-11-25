using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.Controllers;

/// <summary>
/// "Minhas integrações": cada usuário preenche os próprios valores dos plugins de uso pessoal
/// (ex.: token do GitHub/Azure DevOps). Segredos nunca voltam na resposta.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class UserIntegrationController : ControllerBase
{
    private readonly IUserPluginConfigurationApplication _application;
    private readonly IPluginConfigurationResolver _resolver;

    public UserIntegrationController(IUserPluginConfigurationApplication application, IPluginConfigurationResolver resolver)
    {
        _application = application;
        _resolver = resolver;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserIntegrationResponse>>> List(CancellationToken cancellationToken)
    {
        if (_resolver.CurrentUserExternalId is not { } user)
            return Unauthorized(new { error = "Usuário não identificado" });

        return Ok(WithOptions(await _application.ListAsync(user, cancellationToken)));
    }

    /// <summary>0102: campos de valores fechados no catálogo (ex.: Perguntas da Base Solvace) viram lista de escolha.</summary>
    private static IReadOnlyList<UserIntegrationResponse> WithOptions(IReadOnlyList<UserIntegrationResponse> integrations)
    {
        foreach (var integration in integrations)
        foreach (var field in integration.Fields)
        {
            var def = solvace.prform.PluginSettings.PluginSettingsCatalog.Fields.FirstOrDefault(f =>
                f.Plugin.Id is null && string.Equals(f.Plugin.Name, integration.Description, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.Key, field.Key, StringComparison.OrdinalIgnoreCase));
            if (def is { Type: solvace.prform.PluginSettings.SettingTypes.Enum, AllowCustom: false, Options.Count: > 0 } && !field.Sensitive)
                field.Options = def.Options.Select(o => new UserIntegrationFieldOption { Value = o.Value, Label = o.Label }).ToList();
        }
        return integrations;
    }

    [HttpGet("status")]
    public async Task<ActionResult<UserIntegrationStatusResponse>> Status(CancellationToken cancellationToken)
    {
        if (_resolver.CurrentUserExternalId is not { } user)
            return Unauthorized(new { error = "Usuário não identificado" });

        return Ok(await _application.GetStatusAsync(user, cancellationToken));
    }

    /// <summary>
    /// Salva os valores do usuário para um plugin de uso pessoal. Chave omitida ou null mantém o
    /// valor salvo (segredos); string vazia limpa.
    /// </summary>
    [HttpPut("{pluginId:int}")]
    public async Task<ActionResult<UserIntegrationResponse>> Save(int pluginId, SaveUserIntegrationRequest request, CancellationToken cancellationToken)
    {
        if (_resolver.CurrentUserExternalId is not { } user)
            return Unauthorized(new { error = "Usuário não identificado" });

        try
        {
            return Ok(WithOptions([await _application.SaveAsync(user, pluginId, request, cancellationToken)])[0]);
        }
        catch (DomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
        catch (InvalidOperationException e)
        {
            // Chave de criptografia ausente/inválida no servidor: nada é gravado.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = e.Message });
        }
    }
}
