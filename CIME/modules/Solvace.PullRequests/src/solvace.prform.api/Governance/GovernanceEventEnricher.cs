using Microsoft.EntityFrameworkCore;
using solvace.executionplans.infra.Contexts;
using solvace.governance.application.Contracts;
using solvace.governance.domain.Entities;

namespace solvace.prform.Governance;

/// <summary>
/// 0098: completa o lote da trilha antes de gravar — rota do plano (<c>ExecutionPlan/{id}/...</c>) não traz o card, e
/// a do item (<c>WorkItems/{number}</c>) também não: uma consulta por lote (só ids distintos) acha card e fase.
/// </summary>
public sealed class GovernanceEventEnricher(ExecutionPlanContext db) : IGovernanceEventEnricher
{
    public async Task<IReadOnlyList<GovernanceEventData>> EnrichAsync(IReadOnlyList<GovernanceEventData> batch, CancellationToken ct)
    {
        var planIds = batch.Where(e => e.PlanId is not null && (e.CardNumber is null || e.Phase is null))
            .Select(e => e.PlanId!.Value).Distinct().ToList();
        var items = batch.Where(e => e.WorkItemNumber is not null && e.CardNumber is null)
            .Select(e => (long)e.WorkItemNumber!.Value).Distinct().ToList();
        if (planIds.Count == 0 && items.Count == 0) return batch;

        var plans = planIds.Count == 0
            ? []
            : await db.Plans.AsNoTracking().Where(p => planIds.Contains(p.Id))
                .Select(p => new { p.Id, p.CardNumber, p.Phase }).ToDictionaryAsync(p => p.Id, ct);
        var workItems = items.Count == 0
            ? []
            : await db.WorkItems.AsNoTracking().Where(w => items.Contains(w.Number))
                .Select(w => new { w.Number, w.CardNumber }).ToDictionaryAsync(w => w.Number, w => w.CardNumber, ct);

        return batch.Select(e =>
        {
            if (e.PlanId is { } pid && plans.TryGetValue(pid, out var plan))
                e = e with { CardNumber = e.CardNumber ?? Card(plan.CardNumber), Phase = e.Phase ?? plan.Phase };
            if (e.CardNumber is null && e.WorkItemNumber is { } n && workItems.TryGetValue(n, out var card))
                e = e with { CardNumber = Card(card) };
            return e;
        }).ToList();
    }

    private static int? Card(string? value) => int.TryParse(value?.Trim().TrimStart('#'), out var n) && n > 0 ? n : null;
}
