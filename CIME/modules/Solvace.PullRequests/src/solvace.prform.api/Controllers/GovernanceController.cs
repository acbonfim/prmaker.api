using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.governance.application;
using solvace.governance.application.Contracts;
using solvace.governance.domain;
using solvace.governance.domain.Entities;
using solvace.prform.Governance;

namespace solvace.prform.Controllers;

/// <summary>
/// 0098: governança do PRMake. <c>evaluate</c>, <c>hooks/config</c> e <c>hooks/events</c> são a ponta dos hooks do
/// Claude Code (qualquer usuário autenticado, sempre com a identidade de quem chama); o resto — regras, hooks,
/// configurações, trilha e métricas (<see cref="GovernanceMetricsController"/>) — é do administrador.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class GovernanceController(IGovernanceApplication governance, IGovernanceEventSink sink) : ControllerBase
{
    private const int MaxHookEvents = 200;
    private const int MaxInputLength = 4_000;

    /// <summary>Avalia uma ferramenta/comando (hook PreToolUse). Grava evento só quando casa alguma regra.</summary>
    [HttpPost("evaluate")]
    public async Task<ActionResult<EvaluateResponse>> Evaluate([FromBody] EvaluateRequest request, CancellationToken ct)
    {
        var caller = Caller(request.Source);
        var trimmed = request with { Input = Cut(request.Input, MaxInputLength) };
        var engine = GovernanceApplication.ToEngineRequest(trimmed, caller.Source, caller.Actor);
        var decision = await governance.EvaluateAsync(engine, ct);
        if (decision.Rules.Count > 0)
            sink.Enqueue(new GovernanceEventData
            {
                StartedAt = DateTimeOffset.UtcNow,
                DurationMs = 0,
                UserId = caller.Actor.UserId,
                UserName = caller.Actor.Name,
                Role = caller.Role,
                Source = caller.Source,
                Action = engine.Action ?? $"hook:{request.Event}",
                Category = "tool",
                CardNumber = request.CardNumber,
                Outcome = decision.Outcome,
                Rules = decision.Rules.Select(r => r.Key).ToList(),
                Detail = $"{decision.Reason ?? decision.Rules[0].Message}\n{Cut(request.Input ?? request.Path, 500)}",
                SessionId = request.SessionId,
                Machine = request.Machine ?? caller.Machine
            });
        return Ok(EvaluateResponse.From(decision));
    }

    /// <summary>Configuração dos hooks do Claude Code (o script guarda em cache local).</summary>
    [HttpGet("hooks/config")]
    public async Task<ActionResult<HooksConfigResponse>> HooksConfig(CancellationToken ct) => Ok(await governance.GetHooksConfigAsync(ct));

    /// <summary>Lote de eventos dos hooks do Claude Code (terminal ou executor).</summary>
    [HttpPost("hooks/events")]
    public IActionResult HookEvents([FromBody] List<HookEventRequest> events)
    {
        if (events is null || events.Count == 0) return NoContent();
        if (events.Count > MaxHookEvents) return BadRequest(new { message = $"No máximo {MaxHookEvents} eventos por lote." });
        var now = DateTimeOffset.UtcNow;
        foreach (var e in events.Where(e => !string.IsNullOrWhiteSpace(e.Event)))
        {
            var caller = Caller(e.Source ?? GovernanceSource.Hook);
            var evt = e.Event.StartsWith("claude:") ? e.Event["claude:".Length..] : e.Event;
            var started = e.StartedAt is { } s && s <= now.AddMinutes(5) && s > now.AddDays(-7) ? s : now;
            sink.Enqueue(new GovernanceEventData
            {
                StartedAt = started,
                FinishedAt = e.FinishedAt,
                DurationMs = e.DurationMs is >= 0 ? e.DurationMs : null,
                UserId = caller.Actor.UserId,
                UserName = caller.Actor.Name,
                Role = caller.Role,
                Source = caller.Source,
                Action = string.IsNullOrWhiteSpace(e.Tool) ? $"hook:{evt}" : $"hook:{evt}:{e.Tool}",
                Category = evt switch
                {
                    "SessionStart" or "Stop" or "SubagentStop" or "UserPromptSubmit" => "session",
                    _ => "tool"
                },
                CardNumber = e.CardNumber is > 0 ? e.CardNumber : null,
                Outcome = GovernanceOutcome.All.Contains(e.Outcome) ? e.Outcome! : GovernanceOutcome.Ok,
                Rules = e.Rules,
                Detail = Cut(e.Detail ?? e.Input, 1_000),
                SessionId = e.SessionId,
                Machine = e.Machine ?? caller.Machine
            });
        }
        return Accepted();
    }

    // ---- regras -------------------------------------------------------------------------------------------------

    [HttpGet("rules")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Rules(CancellationToken ct) => Ok(await governance.GetRulesAsync(ct));

    [HttpPost("rules")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> CreateRule([FromBody] RuleRequest request, CancellationToken ct) =>
        Guarded(async () => Ok(await governance.CreateRuleAsync(request, Caller().Actor, ct)));

    [HttpPut("rules/{id:guid}")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> UpdateRule(Guid id, [FromBody] RuleRequest request, CancellationToken ct) =>
        Guarded(async () => await governance.UpdateRuleAsync(id, request, Caller().Actor, ct) is { } rule ? Ok(rule) : NotFound());

    [HttpDelete("rules/{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct) =>
        await governance.DeleteRuleAsync(id, Caller().Actor, ct) switch
        {
            null => NotFound(),
            false => Conflict(new { message = "Regra embutida não pode ser apagada — desative ou mude o efeito." }),
            true => NoContent()
        };

    /// <summary>Simulador: avalia a entrada com as regras gravadas (ou só com o rascunho), sem gravar nada.</summary>
    [HttpPost("rules/test")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> TestRule([FromBody] RuleTestRequest request, CancellationToken ct) =>
        Guarded(async () => Ok(await governance.TestRuleAsync(request, Caller().Actor, ct)));

    // ---- hooks --------------------------------------------------------------------------------------------------

    [HttpGet("hooks")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Hooks(CancellationToken ct) =>
        Ok((await governance.GetHooksAsync(ct)).Select(MaskSecrets));

    [HttpPost("hooks")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> CreateHook([FromBody] HookRequest request, CancellationToken ct) =>
        Guarded(async () => Ok(MaskSecrets(await governance.CreateHookAsync(request, Caller().Actor, ct))));

    [HttpPut("hooks/{id:guid}")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> UpdateHook(Guid id, [FromBody] HookRequest request, CancellationToken ct) =>
        Guarded(async () => await governance.UpdateHookAsync(id, request, Caller().Actor, ct) is { } hook ? Ok(MaskSecrets(hook)) : NotFound());

    [HttpDelete("hooks/{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteHook(Guid id, CancellationToken ct) =>
        await governance.DeleteHookAsync(id, Caller().Actor, ct) switch
        {
            null => NotFound(),
            false => Conflict(new { message = "Hook embutido não pode ser apagado — desative." }),
            true => NoContent()
        };

    // ---- configurações e trilha ---------------------------------------------------------------------------------

    [HttpGet("settings")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Settings(CancellationToken ct) => Ok(await governance.GetSettingsAsync(ct));

    [HttpPut("settings")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> UpdateSettings([FromBody] SettingsRequest request, CancellationToken ct) =>
        Guarded(async () => Ok(await governance.UpdateSettingsAsync(request, Caller().Actor, ct)));

    /// <summary>Trilha (mais recente primeiro).</summary>
    [HttpGet("events")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Events([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? user,
        [FromQuery] Guid? userId, [FromQuery] int? card, [FromQuery] string? source, [FromQuery] string? category,
        [FromQuery] string? outcome, [FromQuery] string? action, [FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int size = 50, CancellationToken ct = default)
    {
        var (total, items) = await governance.GetEventsAsync(new GovernanceEventFilter
        {
            From = from, To = to, User = user, UserId = userId, Card = card, Source = source, Category = category,
            Outcome = outcome, Action = action, Search = search, Page = page, Size = size
        }, ct);
        return Ok(new { total, items });
    }

    /// <summary>Histórico de alterações de regras, hooks e configurações (eventos da categoria governance).</summary>
    [HttpGet("changes")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Changes([FromQuery] int take = 50, CancellationToken ct = default)
    {
        var (_, items) = await governance.GetEventsAsync(new GovernanceEventFilter { Category = "governance", Size = Math.Clamp(take, 1, 200) }, ct);
        return Ok(items);
    }

    private GovernanceCaller Caller(string? requested = null)
    {
        var caller = GovernanceCaller.From(HttpContext);
        // O hook diz de onde vem (especialista/executor no executor); o token manda quando é mais específico.
        if (caller.Source is GovernanceSource.Executor or GovernanceSource.Specialist) return caller;
        return requested is GovernanceSource.Hook or GovernanceSource.Executor or GovernanceSource.Specialist or GovernanceSource.Skill
            ? caller with { Source = requested, Actor = caller.Actor with { Source = requested } }
            : caller;
    }

    private async Task<IActionResult> Guarded(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DomainException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static object MaskSecrets(GovernanceHook h) => new
    {
        h.Id, h.Key, h.Name, h.Description, h.Event, h.Matcher, h.Action,
        Config = h.Config.ToDictionary(kv => kv.Key, kv => h.PublicConfig().ContainsKey(kv.Key) ? kv.Value : "***"),
        h.Enabled, h.Builtin, h.Order, h.UpdatedAt, h.UpdatedBy
    };

    private static string? Cut(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max] + "…";
}
