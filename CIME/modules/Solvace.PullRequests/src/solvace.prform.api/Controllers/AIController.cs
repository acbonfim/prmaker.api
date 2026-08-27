using Microsoft.AspNetCore.Mvc;
using solvace.ai.application.Contract;
using solvace.ai.application.Services;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class AIController : ControllerBase
{
    private readonly IAIService _aiService;

    public AIController(IAIService aiService)
    {
        _aiService = aiService;
    }

    [HttpPost("generate")]
    [Consumes("application/json", "text/plain")]
    public async Task<IActionResult> Generate([FromBody] string prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return BadRequest(new { error = "Prompt não pode ser vazio" });
        }

        var result = await _aiService.GenerateContentAsync(prompt, cancellationToken);

        if (result == null)
        {
            return StatusCode(500, new { error = "Erro desconhecido ao gerar conteúdo" });
        }

        if (!string.IsNullOrEmpty(result.Error))
        {
            return StatusCode(500, new { error = result.Error, provider = result.Provider });
        }

        return Ok(result);
    }
}

