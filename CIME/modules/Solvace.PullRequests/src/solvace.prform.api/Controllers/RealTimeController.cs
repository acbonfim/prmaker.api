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
    /// O front chama a cada (re)conexão; <c>url</c> nula => usar a do environment.
    /// </summary>
    [HttpGet("connection")]
    public IActionResult GetConnection() =>
        Ok(_connectionService.GetConnectionInfo(User.FindFirst("ExternalId")?.Value));
}
