using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.governance.application;
using solvace.governance.domain;
using solvace.governance.domain.Entities;
using solvace.prform.Governance;

namespace solvace.prform.Controllers;

/// <summary>
/// 0106: lições aprendidas e sinais de erro. Ler e gravar valem para todo usuário autenticado (os agentes gravam pelo MCP ou
/// por aqui); editar, aposentar e reativar é de admin. Nada aqui é obrigatório para funcionar: a lição gravada já vale.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Governance/lessons")]
[Authorize]
public class GovernanceLessonsController(IGovernanceLessonsApplication lessons) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? skill, [FromQuery] string? search,
        [FromQuery] string? card, CancellationToken ct) =>
        Ok((await lessons.ListAsync(new LessonFilter { Status = status, Scope = skill, Search = search, Card = card }, ct)).Select(View));

    /// <summary>Markdown das lições que valem para a skill (e os sinais abertos do card) — o mesmo que entra nas regras.</summary>
    [HttpGet("markdown")]
    public async Task<IActionResult> Markdown([FromQuery] string? skill, [FromQuery] string? card, [FromQuery] int max = 25, CancellationToken ct = default) =>
        Content(await lessons.MarkdownAsync(skill, card, max, ct), "text/markdown; charset=utf-8");

    [HttpPost]
    public Task<IActionResult> Record([FromBody] LessonInput input, CancellationToken ct) =>
        Guarded(async () =>
        {
            var caller = GovernanceCaller.From(HttpContext);
            var origin = caller.Source == GovernanceSource.Screen ? LessonOrigin.Admin : input.Origin;
            var r = await lessons.RecordAsync(input with { Origin = origin == LessonOrigin.Seed ? LessonOrigin.Agent : origin }, caller.Actor, ct);
            return Ok(new { lesson = View(r.Lesson), reinforced = r.Reinforced, superseded = r.Superseded?.Id });
        });

    [HttpPut("{id:long}")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> Edit(long id, [FromBody] LessonData data, CancellationToken ct) =>
        Guarded(async () => await lessons.EditAsync(id, data, Actor(), ct) is { } l ? Ok(View(l)) : NotFound());

    public sealed record ReasonRequest(string? Reason);

    [HttpPost("{id:long}/retire")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> Retire(long id, [FromBody] ReasonRequest? request, CancellationToken ct) =>
        Guarded(async () => await lessons.RetireAsync(id, request?.Reason, Actor(), ct) is { } l ? Ok(View(l)) : NotFound());

    [HttpPost("{id:long}/reactivate")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> Reactivate(long id, CancellationToken ct) =>
        Guarded(async () => await lessons.ReactivateAsync(id, Actor(), ct) is { } l ? Ok(View(l)) : NotFound());

    [HttpGet("signals")]
    public async Task<IActionResult> Signals([FromQuery] string? card, [FromQuery] string? status, [FromQuery] int take = 100, CancellationToken ct = default) =>
        Ok(await lessons.SignalsAsync(card, status, take, ct));

    [HttpPost("signals/{id:long}/dismiss")]
    public Task<IActionResult> Dismiss(long id, [FromBody] ReasonRequest? request, CancellationToken ct) =>
        Guarded(async () => await lessons.DismissSignalAsync(id, request?.Reason, Actor(), ct) is { } s ? Ok(s) : NotFound());

    private solvace.governance.application.Contracts.GovernanceActor Actor() => GovernanceCaller.From(HttpContext).Actor;

    public static object View(GovernanceLesson l) => new
    {
        l.Id, l.Code, l.Scope, l.Trigger, text = l.Text, l.Why, l.Sources, l.Status, l.Origin, l.Hits, l.CreatedAt, l.CreatedBy, l.LastSeenAt,
        l.UpdatedAt, l.UpdatedBy, l.RetiredReason
    };

    private async Task<IActionResult> Guarded(Func<Task<IActionResult>> body)
    {
        try
        {
            return await body();
        }
        catch (DomainException e)
        {
            return BadRequest(new { message = e.Message });
        }
    }
}
