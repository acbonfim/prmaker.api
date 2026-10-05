using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.prform.Admin;

namespace solvace.prform.Controllers;

/// <summary>
/// 0061: recomeçar um card do zero no PRMake (planos, fila, Timeline, tabela de PR, registro na engenharia reversa) — para
/// refazer a análise, ex. depois de melhorar a engenharia reversa. Só admin. <c>dryRun=true</c> (padrão) só conta.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/ExecutionPlan")]
[Authorize(Roles = "admin")]
public class CardResetController(CardResetService service) : ControllerBase
{
    [HttpPost("card/{cardNumber}/reset")]
    public async Task<ActionResult<CardResetResult>> Reset([FromRoute] string cardNumber, [FromQuery] bool dryRun = true, CancellationToken ct = default)
    {
        var actor = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "admin";
        var result = await service.ResetAsync(cardNumber, dryRun, actor, ct);
        return result.Blocked is null ? Ok(result) : Conflict(result);
    }
}
