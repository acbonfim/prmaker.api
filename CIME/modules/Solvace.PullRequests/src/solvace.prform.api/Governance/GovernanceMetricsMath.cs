using System.Text.RegularExpressions;
using solvace.governance.domain.Engine;
using solvace.governance.domain.Entities;

namespace solvace.prform.Governance;

/// <summary>0098: linhas leves (projeções) que as métricas leem — nunca planos/itens inteiros.</summary>
public sealed record PlanRow(Guid Id, string CardNumber, string Kind, string Phase, string Status, string Title, Guid? UserId,
    string UserName, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);

public sealed record StepRow(Guid PlanId, string Key, string Kind, string Title, string Status, string? ChangedBy,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);

public sealed record WorkItemRow(Guid Id, long Number, string CardNumber, string Type, string Title, string Status, Guid? UserId,
    string UserName, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);

public sealed record TaskRow(Guid WorkItemId, long Number, string Kind, string Title, string Status, DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record RequestRow(Guid Id, string CardNumber, string Kind, string Status, Guid? UserId, string UserName,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, decimal? CostUsd);

public sealed record EventRow(long Id, DateTimeOffset StartedAt, int? DurationMs, Guid? UserId, string UserName, string Source,
    string Action, string Category, string Outcome, string[] Rules, string? Detail);

public sealed record MilestoneHit(string Key, string Label, DateTimeOffset At);

public sealed record TimelineRow(DateTimeOffset At, DateTimeOffset? EndAt, long? DurationMs, string Kind, string Title, string? User,
    string? Outcome, string? Ref);

/// <summary>Tudo de um card que a linha do tempo e os marcos precisam.</summary>
public sealed record CardActivity(
    int Card,
    IReadOnlyList<PlanRow> Plans,
    IReadOnlyList<StepRow> Steps,
    IReadOnlyList<WorkItemRow> WorkItems,
    IReadOnlyList<TaskRow> Tasks,
    IReadOnlyList<RequestRow> Requests,
    IReadOnlyList<EventRow> Events);

/// <summary>0098: contas puras das métricas (mediana, p90, marcos, linha do tempo) — testáveis sem banco.</summary>
public static partial class GovernanceMetricsMath
{
    public static long? Median(IEnumerable<long> values) => Percentile(values, 0.5);

    /// <summary>Percentil por interpolação linear (como o percentile_cont do PostgreSQL).</summary>
    public static long? Percentile(IEnumerable<long> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return null;
        var rank = p * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        return (long)Math.Round(sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo));
    }

    public static long? Ms(DateTimeOffset? start, DateTimeOffset? end) =>
        start is { } s && end is { } e && e >= s ? (long)(e - s).TotalMilliseconds : null;

    [GeneratedRegex(@"(^\d+[-_.]?)|([-_.]?\d+$)")]
    private static partial Regex NumberAffix();

    /// <summary>Chave de etapa sem número de ordem/sufixo ("03-correcao-2" → "correcao").</summary>
    public static string NormalizeStepKey(string key)
    {
        var k = key.Trim().ToLowerInvariant();
        for (var i = 0; i < 3; i++)
        {
            var next = NumberAffix().Replace(k, string.Empty);
            if (next == k || next.Length == 0) break;
            k = next;
        }
        return k;
    }

    /// <summary>Primeira data de cada marco configurado (marco sem data fica de fora).</summary>
    public static List<MilestoneHit> Milestones(CardActivity a, IReadOnlyList<Milestone> defs)
    {
        var hits = new List<MilestoneHit>();
        var planById = a.Plans.ToDictionary(p => p.Id);
        foreach (var m in defs.OrderBy(d => d.Order))
        {
            IEnumerable<DateTimeOffset?> dates = m.From switch
            {
                Milestone.FromPlan => a.Plans.Where(p => m.Phase is null || p.Phase == m.Phase)
                    .Where(p => m.Match is null || GovernanceRuleEngine.Glob(m.Match, p.Kind))
                    .Select(p => Pick(m.At, p.CreatedAt, p.StartedAt, p.FinishedAt is not null && p.Status == "completed" ? p.FinishedAt : null)),
                Milestone.FromStep => a.Steps.Where(s => m.Phase is null || (planById.TryGetValue(s.PlanId, out var pl) && pl.Phase == m.Phase))
                    .Where(s => GovernanceRuleEngine.Glob(m.Match!, s.Key) || GovernanceRuleEngine.Glob(m.Match!, s.Kind))
                    .Select(s => Pick(m.At, s.StartedAt, s.StartedAt, s.Status == "completed" ? s.FinishedAt : null)),
                Milestone.FromWorkItem => a.WorkItems.Where(w => GovernanceRuleEngine.Glob(m.Match!, w.Type))
                    .Select(w => Pick(m.At, w.CreatedAt, w.StartedAt, w.Status == "completed" ? w.CompletedAt : null)),
                Milestone.FromTask => a.Tasks.Where(t => GovernanceRuleEngine.Glob(m.Match!, t.Kind))
                    .Select(t => Pick(m.At, t.StartedAt, t.StartedAt, t.Status == "completed" ? t.CompletedAt : null)),
                Milestone.FromRequest => a.Requests.Where(r => GovernanceRuleEngine.Glob(m.Match!, r.Kind))
                    .Select(r => Pick(m.At, r.CreatedAt, r.StartedAt, r.Status == "done" ? r.FinishedAt : null)),
                Milestone.FromEvent => a.Events.Where(e => e.Outcome is "ok" or "warned" or "monitored" && GovernanceRuleEngine.Glob(m.Match!, e.Action))
                    .Select(e => (DateTimeOffset?)e.StartedAt),
                _ => []
            };
            if (dates.Where(d => d is not null).Select(d => d!.Value).DefaultIfEmpty().Min() is { } first && first != default)
                hits.Add(new MilestoneHit(m.Key, m.Label, first));
        }
        return hits;
    }

    private static DateTimeOffset? Pick(string at, DateTimeOffset? created, DateTimeOffset? started, DateTimeOffset? finished) => at switch
    {
        Milestone.AtStarted => started,
        Milestone.AtFinished => finished,
        _ => created
    };

    /// <summary>Linha do tempo cronológica do card: marcos, planos, etapas, itens, sub-tarefas, pedidos e eventos.</summary>
    public static List<TimelineRow> Timeline(CardActivity a, IReadOnlyList<MilestoneHit> milestones)
    {
        var rows = new List<TimelineRow>();
        rows.AddRange(milestones.Select(m => new TimelineRow(m.At, null, null, "milestone", m.Label, null, null, m.Key)));
        var planById = a.Plans.ToDictionary(p => p.Id);
        rows.AddRange(a.Plans.Select(p => new TimelineRow(p.StartedAt ?? p.CreatedAt, p.FinishedAt, Ms(p.StartedAt ?? p.CreatedAt, p.FinishedAt),
            "plan", $"Plano de {(p.Phase == "correction" ? "correção" : "análise")} — {p.Title}", p.UserName, p.Status, p.Id.ToString())));
        rows.AddRange(a.Steps.Where(s => s.StartedAt is not null).Select(s => new TimelineRow(s.StartedAt!.Value, s.FinishedAt,
            Ms(s.StartedAt, s.FinishedAt), "step",
            $"{(planById.TryGetValue(s.PlanId, out var p) && p.Phase == "correction" ? "Correção" : "Análise")}: {s.Title}",
            s.ChangedBy, s.Status, s.Key)));
        rows.AddRange(a.WorkItems.Select(w => new TimelineRow(w.StartedAt ?? w.CreatedAt, w.CompletedAt, Ms(w.StartedAt ?? w.CreatedAt, w.CompletedAt),
            "work-item", $"Item #{w.Number} — {w.Title}", w.UserName, w.Status, w.Number.ToString())));
        var itemById = a.WorkItems.ToDictionary(w => w.Id);
        rows.AddRange(a.Tasks.Where(t => t.StartedAt is not null).Select(t => new TimelineRow(t.StartedAt!.Value, t.CompletedAt,
            Ms(t.StartedAt, t.CompletedAt), "task",
            $"#{(itemById.TryGetValue(t.WorkItemId, out var w) ? w.Number : 0)}.{t.Number} {t.Title}", null, t.Status, t.Kind)));
        rows.AddRange(a.Requests.Select(r => new TimelineRow(r.CreatedAt, r.FinishedAt, Ms(r.StartedAt ?? r.CreatedAt, r.FinishedAt),
            "request", $"Executor: {r.Kind}{(r.CostUsd is { } c ? $" (US$ {c:0.00})" : string.Empty)}", r.UserName, r.Status, r.Id.ToString())));
        rows.AddRange(a.Events.Select(e => new TimelineRow(e.StartedAt, e.DurationMs is { } d ? e.StartedAt.AddMilliseconds(d) : null, e.DurationMs,
            "event", e.Action, e.UserName, e.Outcome, e.Id.ToString())));
        return rows.OrderBy(r => r.At).ThenBy(r => r.Kind == "milestone" ? 0 : 1).ToList();
    }
}
