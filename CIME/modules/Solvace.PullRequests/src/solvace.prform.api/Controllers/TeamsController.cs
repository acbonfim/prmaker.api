using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.prform.application.Teams;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.Controllers;

/// <summary>
/// Pedido de aprovação de PR no Teams (feature 0007), pelo Workflow que cada usuário configura em
/// "Minhas integrações". Sem configuração: 403 PERSONAL_INTEGRATION_REQUIRED (feature 0002).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class TeamsController : ControllerBase
{
    private readonly ITeamsApprovalService _teams;
    private readonly solvace.timeline.application.Contracts.IUserRepository _users;

    public TeamsController(ITeamsApprovalService teams, solvace.timeline.application.Contracts.IUserRepository users)
    {
        _teams = teams;
        _users = users;
    }

    /// <summary>Se o botão "Pedir aprovação" pode ser usado (plugin existe e o usuário configurou).</summary>
    [HttpGet("status")]
    public async Task<ActionResult<TeamsStatusResponse>> Status(CancellationToken cancellationToken) =>
        Ok(await _teams.GetStatusAsync(cancellationToken));

    [HttpPost("approval")]
    public async Task<ActionResult<TeamsApprovalResponse>> RequestApproval(TeamsApprovalRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var author = await ResolveAuthorNameAsync(cancellationToken);
            return Ok(await _teams.RequestApprovalAsync(request, author, cancellationToken));
        }
        catch (DomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    /// <summary>Nome de quem pede: o nome completo do usuário da api-key (ou o login, na falta dele).</summary>
    private async Task<string> ResolveAuthorNameAsync(CancellationToken cancellationToken)
    {
        if (Guid.TryParse(User.FindFirst("ExternalId")?.Value, out var id) && id != Guid.Empty)
        {
            var name = await _users.GetFullNameAsync(id, cancellationToken);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? string.Empty;
    }
}
