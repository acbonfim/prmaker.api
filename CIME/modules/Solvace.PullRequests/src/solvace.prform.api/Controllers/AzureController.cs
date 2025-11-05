using Microsoft.AspNetCore.Mvc;
using System.Text;
using solvace.azure.application.Contract;
using solvace.azure.domain.Requests;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class AzureController : ControllerBase
{
    private readonly IAzureService _azureService;

    public AzureController(IAzureService azureService)
    {
        _azureService = azureService;
    }

    [HttpGet("card/{id}")]
    public async Task<IActionResult> GetCard(string id, CancellationToken cancellationToken)
    {
        var result = await _azureService.GetCardAsync(id, cancellationToken);
        if (result == null)
            return BadRequest(new { error = "Erro ao buscar o card" });
        return Ok(result);
    }

    [HttpPost("card/{id}/rootcause")]
    [Consumes("application/json", "text/plain", "text/html", "text/markdown")]
    public async Task<IActionResult> UpdateRootCause([FromRoute] string id,[FromBody] UpdateRootCauseRequest body, CancellationToken cancellationToken)
    {

        var result = await _azureService.UpdateRootCauseAsync(id, body, cancellationToken);
        if (result == null)
            return BadRequest(new { error = "Erro ao atualizar a causa raiz" });
        return Ok(result);
    }
}