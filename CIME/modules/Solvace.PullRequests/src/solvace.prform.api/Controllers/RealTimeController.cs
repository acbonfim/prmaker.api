using System.Security.Claims;
using Cime.BuildingBlocks.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class RealTimeController : ControllerBase
{
    private readonly IRealTimeConnectionService _connectionService;

    public RealTimeController(IRealTimeConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    /// <summary>
    /// URL do hub de tempo real e um token de curta duração para conectar (feature 0013).
    /// O front chama a cada (re)conexão; <c>url</c> nula => usar a do environment. O token leva nome e papéis para a
    /// presença ao vivo (0079): só admin acompanha quem está online.
    /// </summary>
    [HttpGet("connection")]
    public IActionResult GetConnection()
    {
        var roles = User.FindAll(ClaimTypes.Role).Concat(User.FindAll("role")).Select(c => c.Value);
        var name = User.Identity?.Name ?? User.FindFirst("name")?.Value;
        return Ok(_connectionService.GetConnectionInfo(User.FindFirst("ExternalId")?.Value, name, roles));
    }
}
