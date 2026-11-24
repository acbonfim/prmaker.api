using Microsoft.EntityFrameworkCore;
using solvace.executionplans.infra.Contexts;
using solvace.governance.application.Contracts;
using solvace.governance.infra.Contexts;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.Governance;

/// <summary>
/// 0098: métricas da governança sobre o que já existe — planos, etapas, itens, sub-tarefas e pedidos do executor
/// (schema execution), consumo de IA (prform) e a trilha (governance). Só projeções leves; contagens/medianas da trilha
/// no banco (GROUP BY, percentile_cont). Período limitado a 366 dias.
/// </summary>
public sealed class GovernanceMetrics(
    ExecutionPlanContext exec,
    GovernanceContext gov,
    DefaultContext prform,
    IGovernancePolicyProvider policy)
{
    public const int MaxDays = 366;
    private const int MaxRows = 50_000;

    public sealed record Period(DateTimeOffset From, DateTimeOffset To);

    public static Period Normalize(DateTimeOffset? from, DateTimeOffset? to)
    {
        var end = to ?? DateTimeOffset.UtcNow;
        var start = from ?? end.AddDays(-30);
        if (start > end) (start, end) = (end, start);
        if (end - start > TimeSpan.FromDays(MaxDays)) start = end.AddDays(-MaxDays);
        return new Period(start.ToUniversalTime(), end.ToUniversalTime());
    }

    // ---- visão geral ----------------------------------------------------------------------------------------------

    public async Task<object> OverviewAsync(Period p, int tzOffsetMinutes, CancellationToken ct)
    {
        var plans = await PlansAsync(p, ct);
        var analysis = plans.Where(x => x.Phase == "analysis" && x.Status == "completed" && In(p, x.FinishedAt)).ToList();
        var correction = plans.Where(x => x.Phase == "correction" && x.Status == "completed" && In(p, x.FinishedAt)).ToList();

        // Do primeiro plano do card até a primeira análise concluída (cards com análise concluída no período).
        var firstAnalysis = new List<long>();
        foreach (var card in analysis.Select(a => a.CardNumber).Distinct())
        {
            var cardPlans = plans.Where(x => x.CardNumber == card).ToList();
            var start = cardPlans.Min(x => x.CreatedAt);
            var done = cardPlans.Where(x => x.Phase == "analysis" && x.Status == "completed" && x.FinishedAt is not null).Min(x => x.FinishedAt);
            if (GovernanceMetricsMath.Ms(start, done) is { } ms) firstAnalysis.Add(ms);
        }

        var workItemsDone = await exec.WorkItems.AsNoTracking()
            .CountAsync(w => w.Status == "completed" && w.CompletedAt >= p.From && w.CompletedAt < p.To, ct);
        var tasksDone = await exec.WorkItemTasks.AsNoTracking()
            .CountAsync(t => t.Status == "completed" && t.CompletedAt >= p.From && t.CompletedAt < p.To, ct);

        var outcomes = await gov.Events.AsNoTracking().Where(e => e.StartedAt >= p.From && e.StartedAt < p.To)
            .GroupBy(e => e.Outcome).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var eventUsers = await gov.Events.AsNoTracking().Where(e => e.StartedAt >= p.From && e.StartedAt < p.To && e.UserId != null)
            .Select(e => e.UserId).Distinct().ToListAsync(ct);
        var eventCards = await gov.Events.AsNoTracking().Where(e => e.StartedAt >= p.From && e.StartedAt < p.To && e.CardNumber != null)
            .Select(e => e.CardNumber!.Value).Distinct().ToListAsync(ct);
        var users = eventUsers.Concat(plans.Select(x => x.UserId)).Where(u => u is not null).Distinct().Count();
        var cards = eventCards.Select(c => c.ToString()).Concat(plans.Select(x => x.CardNumber)).Distinct().Count();

        var offset = TimeSpan.FromMinutes(-tzOffsetMinutes);
        var perDay = await gov.Database.SqlQuery<DayRow>($"""
            SELECT (("StartedAt" AT TIME ZONE 'UTC') + make_interval(mins => {(int)offset.TotalMinutes}))::date AS "Day",
                   count(*)::int AS "Events",
                   count(*) FILTER (WHERE "Outcome" = 'blocked')::int AS "Blocked",
                   count(*) FILTER (WHERE "Outcome" = 'warned')::int AS "Warned",
                   count(*) FILTER (WHERE "Outcome" = 'monitored')::int AS "Monitored",
                   count(*) FILTER (WHERE "Outcome" = 'error')::int AS "Errors"
            FROM governance."GovernanceEvents"
            WHERE "StartedAt" >= {p.From} AND "StartedAt" < {p.To}
            GROUP BY 1 ORDER BY 1
            """).ToListAsync(ct);
        var bySource = await gov.Events.AsNoTracking().Where(e => e.StartedAt >= p.From && e.StartedAt < p.To)
            .GroupBy(e => e.Source).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync(ct);
        var topActions = await gov.Database.SqlQuery<ActionRow>($"""
            SELECT "Action" AS "Key", count(*)::int AS "Count",
                   percentile_cont(0.5) WITHIN GROUP (ORDER BY "DurationMs")::bigint AS "MedianMs"
            FROM governance."GovernanceEvents"
            WHERE "StartedAt" >= {p.From} AND "StartedAt" < {p.To}
            GROUP BY 1 ORDER BY 2 DESC LIMIT 15
            """).ToListAsync(ct);
        var topRules = await gov.Database.SqlQuery<RuleRow>($"""
            SELECT r AS "Key", count(*)::int AS "Count", count(*) FILTER (WHERE e."Outcome" = 'blocked')::int AS "Blocked"
            FROM governance."GovernanceEvents" e, unnest(e."Rules") AS r
            WHERE e."StartedAt" >= {p.From} AND e."StartedAt" < {p.To}
            GROUP BY 1 ORDER BY 2 DESC LIMIT 15
            """).ToListAsync(ct);
        var names = (await policy.GetAsync(ct)).Rules.ToDictionary(r => r.Key, r => r.Name);

        return new
        {
            kpis = new
            {
                events = outcomes.Sum(o => o.Count),
                users,
                cards,
                plansStarted = plans.Count(x => In(p, x.CreatedAt)),
                analysesDone = analysis.Count,
                correctionsDone = correction.Count,
                workItemsDone,
                tasksDone,
                blocked = outcomes.FirstOrDefault(o => o.Key == "blocked")?.Count ?? 0,
                warned = outcomes.FirstOrDefault(o => o.Key == "warned")?.Count ?? 0,
                monitored = outcomes.FirstOrDefault(o => o.Key == "monitored")?.Count ?? 0,
                errors = outcomes.FirstOrDefault(o => o.Key == "error")?.Count ?? 0,
                medianAnalysisMs = GovernanceMetricsMath.Median(analysis.Select(a => GovernanceMetricsMath.Ms(a.StartedAt ?? a.CreatedAt, a.FinishedAt)).OfType<long>()),
                medianCorrectionMs = GovernanceMetricsMath.Median(correction.Select(a => GovernanceMetricsMath.Ms(a.StartedAt ?? a.CreatedAt, a.FinishedAt)).OfType<long>()),
                medianFirstAnalysisMs = GovernanceMetricsMath.Median(firstAnalysis),
                aiCostUsd = await AiCostAsync(p, null, ct)
            },
            perDay = perDay.Select(d => new { day = d.Day.ToString("yyyy-MM-dd"), events = d.Events, blocked = d.Blocked, warned = d.Warned, monitored = d.Monitored, errors = d.Errors }),
            bySource = bySource.Select(s => new { key = s.Key, count = s.Count }),
            topActions = topActions.Select(a => new { key = a.Key, count = a.Count, medianMs = a.MedianMs }),
            topRules = topRules.Select(r => new { key = r.Key, name = names.GetValueOrDefault(r.Key, r.Key), count = r.Count, blocked = r.Blocked })
        };
    }

    // ---- usuários -------------------------------------------------------------------------------------------------

    public async Task<List<UserRow>> UsersAsync(Period p, CancellationToken ct)
    {
        var plans = await PlansAsync(p, ct);
        var items = await exec.WorkItems.AsNoTracking()
            .Where(w => (w.CreatedAt >= p.From && w.CreatedAt < p.To) || (w.CompletedAt >= p.From && w.CompletedAt < p.To))
            .Select(w => new { w.Id, w.CreatedBy, w.CreatedByName, w.CardNumber })
            .Take(MaxRows).ToListAsync(ct);
        var itemIds = items.Select(i => i.Id).ToList();
        var tasks = await exec.WorkItemTasks.AsNoTracking()
            .Where(t => itemIds.Contains(t.WorkItemId) && t.Status == "completed" && t.CompletedAt >= p.From && t.CompletedAt < p.To)
            .Select(t => new { t.WorkItemId, t.StartedAt, t.CompletedAt }).Take(MaxRows).ToListAsync(ct);
        var events = await gov.Database.SqlQuery<UserEventRow>($"""
            SELECT "UserId", max("UserName") AS "UserName", count(*)::int AS "Events",
                   count(*) FILTER (WHERE "Outcome" = 'blocked')::int AS "Blocked",
                   count(*) FILTER (WHERE "Outcome" = 'warned')::int AS "Warned",
                   count(*) FILTER (WHERE "Outcome" = 'error')::int AS "Errors",
                   count(DISTINCT "CardNumber")::int AS "Cards",
                   max("StartedAt") AS "LastAt"
            FROM governance."GovernanceEvents"
            WHERE "StartedAt" >= {p.From} AND "StartedAt" < {p.To} AND "UserId" IS NOT NULL
            GROUP BY "UserId"
            """).ToListAsync(ct);
        var cost = await AiCostByUserAsync(p, ct);

        var ids = plans.Select(x => x.UserId).Concat(items.Select(i => i.CreatedBy)).Concat(events.Select(e => (Guid?)e.UserId))
            .Where(u => u is not null).Select(u => u!.Value).Distinct();
        var itemOwner = items.ToDictionary(i => i.Id, i => i.CreatedBy);
        return ids.Select(id =>
        {
            var mine = plans.Where(x => x.UserId == id).ToList();
            var analyses = mine.Where(x => x.Phase == "analysis" && x.Status == "completed" && In(p, x.FinishedAt)).ToList();
            var myItems = items.Where(i => i.CreatedBy == id).ToList();
            var myTasks = tasks.Where(t => itemOwner.GetValueOrDefault(t.WorkItemId) == id).ToList();
            var ev = events.FirstOrDefault(e => e.UserId == id);
            var name = mine.Select(x => x.UserName).Concat(myItems.Select(i => i.CreatedByName)).Append(ev?.UserName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Desconhecido";
            var cardSet = mine.Select(x => x.CardNumber).Concat(myItems.Select(i => i.CardNumber)).Distinct().Count();
            var last = new[] { ev?.LastAt, mine.Select(x => (DateTimeOffset?)(x.FinishedAt ?? x.StartedAt ?? x.CreatedAt)).DefaultIfEmpty().Max() }.Max();
            return new UserRow(id, name, ev?.Events ?? 0, Math.Max(cardSet, ev?.Cards ?? 0), mine.Count(x => In(p, x.CreatedAt)), analyses.Count,
                GovernanceMetricsMath.Median(analyses.Select(a => GovernanceMetricsMath.Ms(a.StartedAt ?? a.CreatedAt, a.FinishedAt)).OfType<long>()),
                myItems.Count, myTasks.Count, GovernanceMetricsMath.Median(myTasks.Select(t => GovernanceMetricsMath.Ms(t.StartedAt, t.CompletedAt)).OfType<long>()),
                ev?.Blocked ?? 0, ev?.Warned ?? 0, ev?.Errors ?? 0, cost.GetValueOrDefault(id), last);
        }).OrderByDescending(u => u.LastActivityAt).ToList();
    }

    public async Task<object?> UserAsync(Guid userId, Period p, CancellationToken ct)
    {
        var row = (await UsersAsync(p, ct)).FirstOrDefault(u => u.UserId == userId);
        if (row is null) return null;
        var cardNumbers = (await exec.Plans.AsNoTracking().Where(x => x.CreatedByUserId == userId && x.CreatedAt >= p.From && x.CreatedAt < p.To)
                .Select(x => x.CardNumber).Distinct().ToListAsync(ct))
            .Concat((await gov.Events.AsNoTracking().Where(e => e.UserId == userId && e.CardNumber != null && e.StartedAt >= p.From && e.StartedAt < p.To)
                .Select(e => e.CardNumber!.Value).Distinct().ToListAsync(ct)).Select(c => c.ToString()))
            .Distinct().Take(100).ToList();
        var cards = await CardRowsAsync(cardNumbers.Select(Card).OfType<int>().ToList(), ct);
        var recent = await gov.Events.AsNoTracking().Where(e => e.UserId == userId).OrderByDescending(e => e.StartedAt).Take(50).ToListAsync(ct);
        return new { user = row, cards = cards.OrderByDescending(c => c.LastActivityAt), recent };
    }

    // ---- cards ----------------------------------------------------------------------------------------------------

    public async Task<object> CardsAsync(Period p, string? search, Guid? user, int page, int size, CancellationToken ct)
    {
        var plans = await PlansAsync(p, ct);
        var planCards = plans.Where(x => user is null || x.UserId == user).Select(x => x.CardNumber);
        var itemCards = await exec.WorkItems.AsNoTracking()
            .Where(w => (w.CreatedAt >= p.From && w.CreatedAt < p.To) || (w.CompletedAt >= p.From && w.CompletedAt < p.To))
            .Where(w => user == null || w.CreatedBy == user)
            .Select(w => w.CardNumber).Distinct().ToListAsync(ct);
        var eventCards = await gov.Events.AsNoTracking()
            .Where(e => e.StartedAt >= p.From && e.StartedAt < p.To && e.CardNumber != null && (user == null || e.UserId == user))
            .GroupBy(e => e.CardNumber!.Value).Select(g => new { Card = g.Key, Last = g.Max(e => e.StartedAt) }).ToListAsync(ct);

        var last = new Dictionary<int, DateTimeOffset>();
        void Touch(int card, DateTimeOffset at) { if (!last.TryGetValue(card, out var cur) || at > cur) last[card] = at; }
        foreach (var x in plans) if (Card(x.CardNumber) is { } c) Touch(c, x.FinishedAt ?? x.StartedAt ?? x.CreatedAt);
        foreach (var c in itemCards.Select(Card).OfType<int>()) if (!last.ContainsKey(c)) Touch(c, p.From);
        foreach (var e in eventCards) Touch(e.Card, e.Last);
        var allowed = planCards.Concat(itemCards).Select(Card).OfType<int>().Concat(eventCards.Select(e => e.Card)).ToHashSet();

        var titles = plans.GroupBy(x => x.CardNumber).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First().Title);
        var candidates = last.Where(kv => allowed.Contains(kv.Key)).Select(kv => kv.Key);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().TrimStart('#').ToLowerInvariant();
            candidates = candidates.Where(c => c.ToString().Contains(s) || titles.GetValueOrDefault(c.ToString(), "").ToLowerInvariant().Contains(s));
        }
        var ordered = candidates.OrderByDescending(c => last[c]).ToList();
        size = Math.Clamp(size, 1, 100);
        page = Math.Max(1, page);
        var pageCards = ordered.Skip((page - 1) * size).Take(size).ToList();
        var rows = await CardRowsAsync(pageCards, ct);
        return new { total = ordered.Count, items = pageCards.Select(c => rows.First(r => r.CardNumber == c)) };
    }

    public async Task<object?> CardAsync(int card, CancellationToken ct)
    {
        var activity = await ActivityAsync([card], ct);
        if (!activity.TryGetValue(card, out var a) || (a.Plans.Count == 0 && a.WorkItems.Count == 0 && a.Events.Count == 0)) return null;
        var defs = (await policy.GetAsync(ct)).Settings.Milestones;
        var milestones = GovernanceMetricsMath.Milestones(a, defs);
        return new { card = Row(a, milestones), timeline = GovernanceMetricsMath.Timeline(a, milestones) };
    }

    // ---- tarefas --------------------------------------------------------------------------------------------------

    public async Task<List<TaskKindRow>> TasksAsync(Period p, CancellationToken ct)
    {
        var steps = await exec.Steps.AsNoTracking()
            .Where(s => s.StartedAt >= p.From && s.StartedAt < p.To)
            .Select(s => new { s.Key, s.Kind, s.Status, s.StatusChangedBy, s.StartedAt, s.FinishedAt })
            .Take(MaxRows).ToListAsync(ct);
        var tasks = await exec.WorkItemTasks.AsNoTracking()
            .Where(t => t.StartedAt >= p.From && t.StartedAt < p.To)
            .Join(exec.WorkItems, t => t.WorkItemId, w => w.Id, (t, w) => new { t.Kind, t.Status, t.StartedAt, t.CompletedAt, w.CreatedBy })
            .Take(MaxRows).ToListAsync(ct);
        var requests = await exec.Requests.AsNoTracking()
            .Where(r => r.CreatedAt >= p.From && r.CreatedAt < p.To)
            .Select(r => new { r.Kind, r.Status, r.RequestedByUserId, r.CreatedAt, r.StartedAt, r.FinishedAt })
            .Take(MaxRows).ToListAsync(ct);

        var result = new List<TaskKindRow>();
        result.AddRange(steps.GroupBy(s => GovernanceMetricsMath.NormalizeStepKey(s.Key)).Select(g => Kind(g.Key, "step",
            g.Count(), g.Count(s => s.Status == "completed"), g.Count(s => s.Status == "failed"),
            g.Where(s => s.Status == "completed").Select(s => GovernanceMetricsMath.Ms(s.StartedAt, s.FinishedAt)).OfType<long>(),
            g.Select(s => s.StatusChangedBy).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().Count())));
        result.AddRange(tasks.GroupBy(t => t.Kind).Select(g => Kind(g.Key, "task",
            g.Count(), g.Count(t => t.Status == "completed"), g.Count(t => t.Status == "failed"),
            g.Where(t => t.Status == "completed").Select(t => GovernanceMetricsMath.Ms(t.StartedAt, t.CompletedAt)).OfType<long>(),
            g.Select(t => t.CreatedBy).Where(u => u is not null).Distinct().Count())));
        result.AddRange(requests.GroupBy(r => r.Kind).Select(g => Kind(g.Key, "request",
            g.Count(), g.Count(r => r.Status == "done"), g.Count(r => r.Status is "failed" or "expired"),
            g.Where(r => r.Status == "done").Select(r => GovernanceMetricsMath.Ms(r.StartedAt ?? r.CreatedAt, r.FinishedAt)).OfType<long>(),
            g.Select(r => r.RequestedByUserId).Where(u => u is not null).Distinct().Count())));
        return result.OrderByDescending(r => r.Count).ToList();
    }

    private static TaskKindRow Kind(string kind, string source, int count, int done, int failed, IEnumerable<long> durations, int users)
    {
        var d = durations.ToList();
        return new TaskKindRow(kind, source, count, done, failed, GovernanceMetricsMath.Median(d), GovernanceMetricsMath.Percentile(d, 0.9), users);
    }

    // ---- composição por card --------------------------------------------------------------------------------------

    private async Task<List<CardRow>> CardRowsAsync(IReadOnlyList<int> cards, CancellationToken ct)
    {
        if (cards.Count == 0) return [];
        var activity = await ActivityAsync(cards, ct);
        var defs = (await policy.GetAsync(ct)).Settings.Milestones;
        return cards.Where(activity.ContainsKey).Select(c => Row(activity[c], GovernanceMetricsMath.Milestones(activity[c], defs))).ToList();
    }

    private static CardRow Row(CardActivity a, List<MilestoneHit> milestones)
    {
        var dates = a.Plans.SelectMany(p => new[] { (DateTimeOffset?)p.CreatedAt, p.FinishedAt })
            .Concat(a.WorkItems.SelectMany(w => new[] { (DateTimeOffset?)w.CreatedAt, w.CompletedAt }))
            .Concat(a.Events.Select(e => (DateTimeOffset?)e.StartedAt))
            .Concat(a.Requests.Select(r => (DateTimeOffset?)r.CreatedAt))
            .OfType<DateTimeOffset>().ToList();
        var first = dates.Count > 0 ? dates.Min() : (DateTimeOffset?)null;
        var lastAt = dates.Count > 0 ? dates.Max() : (DateTimeOffset?)null;
        var latest = a.Plans.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        long? Sum(string phase) => a.Plans.Where(p => p.Phase == phase)
            .Select(p => GovernanceMetricsMath.Ms(p.StartedAt ?? p.CreatedAt, p.FinishedAt)).OfType<long>().DefaultIfEmpty().Sum() is var s && s > 0 ? s : null;
        var closed = milestones.FirstOrDefault(m => m.Key == "closed")?.At;
        var users = a.Plans.Select(p => p.UserName).Concat(a.WorkItems.Select(w => w.UserName)).Concat(a.Events.Select(e => e.UserName))
            .Where(n => !string.IsNullOrWhiteSpace(n) && n != "Desconhecido").Distinct().Take(20).ToList();
        return new CardRow(a.Card, latest?.Title ?? a.WorkItems.FirstOrDefault()?.Title ?? string.Empty, latest?.Kind, users, first, lastAt,
            milestones.Select(m => new { key = m.Key, label = m.Label, at = m.At }).ToList<object>(),
            Sum("analysis"), Sum("correction"), GovernanceMetricsMath.Ms(first, closed ?? lastAt),
            a.WorkItems.Count, a.Tasks.Count(t => t.Status == "completed"), a.Events.Count(e => e.Outcome == "blocked"),
            latest?.Status ?? a.WorkItems.OrderByDescending(w => w.CreatedAt).FirstOrDefault()?.Status);
    }

    private async Task<Dictionary<int, CardActivity>> ActivityAsync(IReadOnlyList<int> cards, CancellationToken ct)
    {
        var keys = cards.Select(c => c.ToString()).ToList();
        var plans = await exec.Plans.AsNoTracking().Where(x => keys.Contains(x.CardNumber))
            .Select(x => new PlanRow(x.Id, x.CardNumber, x.Kind, x.Phase, x.Status, x.Title, x.CreatedByUserId, x.CreatedBy, x.CreatedAt, x.StartedAt, x.FinishedAt))
            .ToListAsync(ct);
        var planIds = plans.Select(x => x.Id).ToList();
        var steps = await exec.Steps.AsNoTracking().Where(s => planIds.Contains(s.PlanId))
            .Select(s => new StepRow(s.PlanId, s.Key, s.Kind, s.Title, s.Status, s.StatusChangedBy, s.StartedAt, s.FinishedAt))
            .Take(MaxRows).ToListAsync(ct);
        var items = await exec.WorkItems.AsNoTracking().Where(w => keys.Contains(w.CardNumber))
            .Select(w => new WorkItemRow(w.Id, w.Number, w.CardNumber, w.Type, w.Title, w.Status, w.CreatedBy, w.CreatedByName, w.CreatedAt, w.StartedAt, w.CompletedAt))
            .ToListAsync(ct);
        var itemIds = items.Select(w => w.Id).ToList();
        var tasks = await exec.WorkItemTasks.AsNoTracking().Where(t => itemIds.Contains(t.WorkItemId))
            .Select(t => new TaskRow(t.WorkItemId, t.Number, t.Kind, t.Title, t.Status, t.StartedAt, t.CompletedAt))
            .Take(MaxRows).ToListAsync(ct);
        var requests = await exec.Requests.AsNoTracking().Where(r => keys.Contains(r.CardNumber))
            .Select(r => new RequestRow(r.Id, r.CardNumber, r.Kind, r.Status, r.RequestedByUserId, r.RequestedBy, r.CreatedAt, r.StartedAt, r.FinishedAt, r.CostUsd))
            .Take(MaxRows).ToListAsync(ct);
        var cardInts = cards.ToList();
        var events = await gov.Events.AsNoTracking().Where(e => e.CardNumber != null && cardInts.Contains(e.CardNumber.Value))
            .OrderByDescending(e => e.StartedAt).Take(500 * Math.Max(1, cards.Count))
            .Select(e => new { e.CardNumber, Row = new EventRow(e.Id, e.StartedAt, e.DurationMs, e.UserId, e.UserName, e.Source, e.Action, e.Category, e.Outcome, e.Rules, e.Detail) })
            .ToListAsync(ct);

        var itemCard = items.ToDictionary(w => w.Id, w => w.CardNumber);
        return cards.ToDictionary(c => c, c =>
        {
            var key = c.ToString();
            var cardPlans = plans.Where(x => x.CardNumber == key).ToList();
            var ids = cardPlans.Select(x => x.Id).ToHashSet();
            return new CardActivity(c, cardPlans, steps.Where(s => ids.Contains(s.PlanId)).ToList(),
                items.Where(w => w.CardNumber == key).ToList(),
                tasks.Where(t => itemCard.GetValueOrDefault(t.WorkItemId) == key).ToList(),
                requests.Where(r => r.CardNumber == key).ToList(),
                events.Where(e => e.CardNumber == c).Select(e => e.Row).Take(500).ToList());
        });
    }

    // ---- apoio ----------------------------------------------------------------------------------------------------

    private Task<List<PlanRow>> PlansAsync(Period p, CancellationToken ct) =>
        exec.Plans.AsNoTracking()
            .Where(x => (x.CreatedAt >= p.From && x.CreatedAt < p.To) || (x.FinishedAt >= p.From && x.FinishedAt < p.To))
            .Select(x => new PlanRow(x.Id, x.CardNumber, x.Kind, x.Phase, x.Status, x.Title, x.CreatedByUserId, x.CreatedBy, x.CreatedAt, x.StartedAt, x.FinishedAt))
            .Take(MaxRows).ToListAsync(ct);

    private async Task<decimal> AiCostAsync(Period p, Guid? user, CancellationToken ct)
    {
        var ai = await prform.AiUsageRecords.AsNoTracking()
            .Where(r => r.CreatedAt >= p.From && r.CreatedAt < p.To && (user == null || r.UserExternalId == user))
            .SumAsync(r => r.CostUsd ?? 0, ct);
        var executor = await exec.Requests.AsNoTracking()
            .Where(r => r.CreatedAt >= p.From && r.CreatedAt < p.To && (user == null || r.RequestedByUserId == user))
            .SumAsync(r => r.CostUsd ?? 0, ct);
        return Math.Round(ai + executor, 2);
    }

    private async Task<Dictionary<Guid, decimal>> AiCostByUserAsync(Period p, CancellationToken ct)
    {
        var ai = await prform.AiUsageRecords.AsNoTracking()
            .Where(r => r.CreatedAt >= p.From && r.CreatedAt < p.To && r.UserExternalId != null)
            .GroupBy(r => r.UserExternalId!.Value).Select(g => new { g.Key, Cost = g.Sum(r => r.CostUsd ?? 0) }).ToListAsync(ct);
        var executor = await exec.Requests.AsNoTracking()
            .Where(r => r.CreatedAt >= p.From && r.CreatedAt < p.To && r.RequestedByUserId != null)
            .GroupBy(r => r.RequestedByUserId!.Value).Select(g => new { g.Key, Cost = g.Sum(r => r.CostUsd ?? 0) }).ToListAsync(ct);
        return ai.Concat(executor).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.Cost), 2));
    }

    private static bool In(Period p, DateTimeOffset? at) => at is { } a && a >= p.From && a < p.To;

    private static int? Card(string? value) => int.TryParse(value?.Trim().TrimStart('#'), out var n) && n > 0 ? n : null;

    public sealed record DayRow(DateTime Day, int Events, int Blocked, int Warned, int Monitored, int Errors);
    public sealed record ActionRow(string Key, int Count, long? MedianMs);
    public sealed record RuleRow(string Key, int Count, int Blocked);
    public sealed record UserEventRow(Guid UserId, string UserName, int Events, int Blocked, int Warned, int Errors, int Cards, DateTimeOffset LastAt);
}

public sealed record UserRow(Guid UserId, string UserName, int Events, int Cards, int PlansStarted, int AnalysesDone, long? MedianAnalysisMs,
    int WorkItems, int TasksDone, long? MedianTaskMs, int Blocked, int Warned, int Errors, decimal AiCostUsd, DateTimeOffset? LastActivityAt);

public sealed record CardRow(int CardNumber, string Title, string? Kind, IReadOnlyList<string> Users, DateTimeOffset? FirstActivityAt,
    DateTimeOffset? LastActivityAt, IReadOnlyList<object> Milestones, long? AnalysisMs, long? CorrectionMs, long? LeadTimeMs, int WorkItems,
    int TasksDone, int Blocked, string? Status);

public sealed record TaskKindRow(string Kind, string Source, int Count, int Done, int Failed, long? MedianMs, long? P90Ms, int Users);
