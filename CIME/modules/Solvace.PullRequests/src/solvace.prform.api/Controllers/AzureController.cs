using Microsoft.AspNetCore.Mvc;
using System.Text;
using solvace.azure.application.Contract;

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
        return new ContentResult { StatusCode = result.StatusCode, Content = result.Content, ContentType = result.ContentType };
    }

    [HttpPost("card/{id}/rootcause")]
    [Consumes("application/json", "text/plain", "text/html", "text/markdown")]
    public async Task<IActionResult> UpdateRootCause(string id, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(cancellationToken);
        var result = await _azureService.UpdateRootCauseAsync(id, raw, cancellationToken);
        return new ContentResult { StatusCode = result.StatusCode, Content = result.Content, ContentType = result.ContentType };
    }
}