using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.prform.Governance;

namespace solvace.prform.Controllers;

/// <summary>
/// 0098: métricas da governança (admin) — visão geral, por usuário, por card (com a linha do tempo e os marcos: quando
/// iniciou, análise inicial, protótipo, correção, PR, QA, fechado, e quanto levou cada ação) e por tipo de tarefa.
/// Mesmo prefixo do <see cref="GovernanceController"/>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Governance")]
[Authorize(Roles = "admin")]
public class GovernanceMetricsController(GovernanceMetrics metrics) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] int tzOffsetMinutes = 180, CancellationToken ct = default) =>
        Ok(await metrics.OverviewAsync(GovernanceMetrics.Normalize(from, to), Math.Clamp(tzOffsetMinutes, -840, 840), ct));

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct = default) =>
        Ok(await metrics.UsersAsync(GovernanceMetrics.Normalize(from, to), ct));

    [HttpGet("users/{userId:guid}")]
    public async Task<IActionResult> User(Guid userId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        CancellationToken ct = default) =>
        await metrics.UserAsync(userId, GovernanceMetrics.Normalize(from, to), ct) is { } user ? Ok(user) : NotFound();

    [HttpGet("cards")]
    public async Task<IActionResult> Cards([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? search,
        [FromQuery] Guid? user, [FromQuery] int page = 1, [FromQuery] int size = 25, CancellationToken ct = default) =>
        Ok(await metrics.CardsAsync(GovernanceMetrics.Normalize(from, to), search, user, page, size, ct));

    [HttpGet("cards/{card:int}")]
    public async Task<IActionResult> Card(int card, CancellationToken ct = default) =>
        await metrics.CardAsync(card, ct) is { } result ? Ok(result) : NotFound();

    [HttpGet("tasks")]
    public async Task<IActionResult> Tasks([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct = default) =>
        Ok(await metrics.TasksAsync(GovernanceMetrics.Normalize(from, to), ct));
}
