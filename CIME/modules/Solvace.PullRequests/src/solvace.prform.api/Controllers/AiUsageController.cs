using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using solvace.prform.AiUsage;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.Controllers;

/// <summary>
/// Consumo de IA (0042): tokens e custo estimado de cada ação de IA. <c>me</c> = o do usuário logado (a chave do plugin
/// pessoal é dele); a raiz = de todos, por usuário (admin).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class AiUsageController(DefaultContext db, solvace.prform.application.IPluginCacheManager plugins) : ControllerBase
{
    /// <summary>
    /// Tabela de preços (US$ por milhão de tokens) do "AI Configurations" com as partes da entrada já resolvidas (0044):
    /// a tela estima o custo do Claude Code (plano, pedidos, relatório) e mostra o desconto do cache lido.
    /// </summary>
    [HttpGet("prices")]
    public async Task<IActionResult> Prices(CancellationToken cancellationToken)
    {
        var prices = await AiUsageRecorder.LoadPricesAsync(plugins, cancellationToken);
        return Ok(new
        {
            prices = prices.ToDictionary(p => p.Key, p => new { input = p.Value.Input, output = p.Value.Output, cacheRead = p.Value.CacheRead, cacheWrite = p.Value.CacheWrite }),
            defaultCacheReadFactor = AiUsagePricing.DefaultCacheReadFactor,
            defaultCacheWriteFactor = AiUsagePricing.DefaultCacheWriteFactor
        });
    }

    private const int MaxDays = 90;
    private const int RecentCount = 50;

    /// <summary>Consumo do usuário logado nos últimos <paramref name="days"/> dias.</summary>
    /// <param name="days">1–90 (padrão 30).</param>
    /// <param name="tzOffsetMinutes">Fuso do navegador em minutos (Brasília = -180) para agrupar por dia.</param>
    [HttpGet("me")]
    public async Task<IActionResult> Mine([FromQuery] int days = 30, [FromQuery] int tzOffsetMinutes = -180, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirst("ExternalId")?.Value, out var me)) return Forbid();
        return Ok(await SummaryAsync(me, days, tzOffsetMinutes, cancellationToken));
    }

    /// <summary>Consumo de todos os usuários (admin), com o total por usuário.</summary>
    [HttpGet]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> All([FromQuery] int days = 30, [FromQuery] int tzOffsetMinutes = -180, CancellationToken cancellationToken = default) =>
        Ok(await SummaryAsync(null, days, tzOffsetMinutes, cancellationToken));

    private async Task<object> SummaryAsync(Guid? user, int days, int tzOffsetMinutes, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var offset = TimeSpan.FromMinutes(Math.Clamp(tzOffsetMinutes, -14 * 60, 14 * 60));
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var query = db.AiUsageRecords.AsNoTracking().Where(r => r.CreatedAt >= since);
        if (user is { } id) query = query.Where(r => r.UserExternalId == id);
        var rows = await query.OrderByDescending(r => r.CreatedAt)
            .Select(r => new Row(r.CreatedAt, r.UserExternalId, r.UserName, r.Action, r.Provider, r.Model, r.InputTokens, r.OutputTokens,
                r.CostUsd, r.DurationMs, r.Success))
            .ToListAsync(cancellationToken);

        return new
        {
            days,
            since,
            total = Totals(rows),
            byAction = rows.GroupBy(r => r.Action)
                .Select(g => new { action = g.Key, label = AiUsageActions.Label(g.Key), totals = Totals(g) })
                .OrderByDescending(a => a.totals.costUsd).ThenByDescending(a => a.totals.calls).ToList(),
            byModel = rows.GroupBy(r => r.Model ?? "")
                .Select(g => new { model = g.Key, totals = Totals(g) })
                .OrderByDescending(m => m.totals.costUsd).ToList(),
            byDay = rows.GroupBy(r => DateOnly.FromDateTime(r.CreatedAt.ToOffset(offset).DateTime))
                .Select(g => new { day = g.Key, totals = Totals(g) })
                .OrderBy(d => d.day).ToList(),
            byUser = user is null
                ? rows.GroupBy(r => (r.UserExternalId, r.UserName))
                    .Select(g => new { userExternalId = g.Key.UserExternalId, userName = g.Key.UserName, totals = Totals(g) })
                    .OrderByDescending(u => u.totals.costUsd).ToList()
                : null,
            recent = rows.Take(RecentCount).Select(r => new
            {
                createdAt = r.CreatedAt,
                action = r.Action,
                label = AiUsageActions.Label(r.Action),
                userName = user is null ? r.UserName : null,
                provider = r.Provider,
                model = r.Model,
                inputTokens = r.InputTokens,
                outputTokens = r.OutputTokens,
                costUsd = r.CostUsd,
                durationMs = r.DurationMs,
                success = r.Success,
            }).ToList(),
        };
    }

    private static TotalsDto Totals(IEnumerable<Row> rows)
    {
        var list = rows as IReadOnlyCollection<Row> ?? rows.ToList();
        return new TotalsDto(list.Count, list.Sum(r => (long)r.InputTokens), list.Sum(r => (long)r.OutputTokens),
            Math.Round(list.Sum(r => r.CostUsd ?? 0m), 4), list.Count(r => r.CostUsd is null), list.Count(r => !r.Success));
    }

    private sealed record Row(DateTimeOffset CreatedAt, Guid? UserExternalId, string UserName, string Action, string Provider, string? Model,
        int InputTokens, int OutputTokens, decimal? CostUsd, int DurationMs, bool Success);

    /// <param name="unpriced">Chamadas de modelo fora da tabela de preços (só tokens).</param>
    private sealed record TotalsDto(int calls, long inputTokens, long outputTokens, decimal costUsd, int unpriced, int failures);
}
