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

        return Ok(await _application.ListAsync(user, cancellationToken));
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
            return Ok(await _application.SaveAsync(user, pluginId, request, cancellationToken));
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
