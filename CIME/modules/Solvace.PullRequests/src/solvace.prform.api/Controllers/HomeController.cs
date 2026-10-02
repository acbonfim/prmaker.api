using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.prform.Home;

namespace solvace.prform.Controllers;

/// <summary>Listas da home (0051): "Seus últimos cards" e "Cards que você participou".</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class HomeController(HomeCardsService cards) : ControllerBase
{
    /// <summary>
    /// <c>scope=mine</c>: cards registrados pelo usuário; <c>scope=participated</c>: cards em que ele salvou, abriu PR,
    /// escreveu na Timeline, mexeu no plano ou salvou o handover (sem os dele). Cada card vem com envolvidos, PRs,
    /// plano, Timeline, comentários e a última atividade.
    /// </summary>
    [HttpGet("cards")]
    public async Task<ActionResult<IReadOnlyList<HomeCardResponse>>> GetCards(
        [FromQuery] string? scope, CancellationToken cancellationToken, [FromQuery] int take = 10)
    {
        if (!Guid.TryParse(User.FindFirst("ExternalId")?.Value, out var userId))
            return Ok(Array.Empty<HomeCardResponse>());
        return Ok(await cards.GetAsync(userId, scope, take, cancellationToken));
    }
}
