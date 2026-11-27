using Microsoft.EntityFrameworkCore;
using solvace.governance.application.Contracts;
using solvace.governance.domain.Entities;
using solvace.governance.infra.Contexts;

namespace solvace.governance.infra.Repositories;

public class GovernanceRepository(GovernanceContext db) : IGovernanceRepository
{
    public Task<List<GovernanceRule>> GetRulesAsync(CancellationToken ct = default) =>
        db.Rules.OrderBy(r => r.Category).ThenBy(r => r.Order).ThenBy(r => r.Key).ToListAsync(ct);

    public Task<GovernanceRule?> GetRuleAsync(Guid id, CancellationToken ct = default) =>
        db.Rules.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<bool> RuleKeyExistsAsync(string key, CancellationToken ct = default) => db.Rules.AnyAsync(r => r.Key == key, ct);

    public void AddRule(GovernanceRule rule) => db.Rules.Add(rule);

    public void RemoveRule(GovernanceRule rule) => db.Rules.Remove(rule);

    public Task<List<GovernanceHook>> GetHooksAsync(CancellationToken ct = default) =>
        db.Hooks.OrderBy(h => h.Order).ThenBy(h => h.Key).ToListAsync(ct);

    public Task<GovernanceHook?> GetHookAsync(Guid id, CancellationToken ct = default) =>
        db.Hooks.FirstOrDefaultAsync(h => h.Id == id, ct);

    public Task<bool> HookKeyExistsAsync(string key, CancellationToken ct = default) => db.Hooks.AnyAsync(h => h.Key == key, ct);

    public void AddHook(GovernanceHook hook) => db.Hooks.Add(hook);

    public void RemoveHook(GovernanceHook hook) => db.Hooks.Remove(hook);

    public async Task<GovernanceSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == GovernanceSettings.SingletonId, ct);
        if (settings is not null) return settings;
        settings = new GovernanceSettings();
        db.Settings.Add(settings);
        return settings;
    }

    public Task<List<GovernanceStandard>> GetStandardsAsync(CancellationToken ct = default) =>
        db.Standards.OrderBy(x => x.Area).ThenBy(x => x.Order).ThenBy(x => x.Key).ToListAsync(ct);

    public Task<GovernanceStandard?> GetStandardAsync(Guid id, CancellationToken ct = default) =>
        db.Standards.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void AddStandard(GovernanceStandard standard) => db.Standards.Add(standard);

    public void RemoveStandard(GovernanceStandard standard) => db.Standards.Remove(standard);

    public Task<GovernanceStandardDraft?> GetDraftAsync(Guid id, CancellationToken ct = default) =>
        db.StandardDrafts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void AddDraft(GovernanceStandardDraft draft) => db.StandardDrafts.Add(draft);

    public Task<List<GovernanceLesson>> GetLessonsAsync(CancellationToken ct = default) =>
        db.Lessons.OrderByDescending(x => x.Hits).ThenByDescending(x => x.LastSeenAt).ToListAsync(ct);

    public Task<GovernanceLesson?> GetLessonAsync(long id, CancellationToken ct = default) =>
        db.Lessons.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void AddLesson(GovernanceLesson lesson) => db.Lessons.Add(lesson);

    public Task<List<GovernanceLessonSignal>> GetSignalsAsync(string? card, string? status, int take, CancellationToken ct = default)
    {
        var q = db.LessonSignals.AsQueryable();
        if (!string.IsNullOrWhiteSpace(card)) q = q.Where(x => x.Card == card);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        return q.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
    }

    public Task<GovernanceLessonSignal?> GetSignalAsync(long id, CancellationToken ct = default) =>
        db.LessonSignals.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> SignalKeyExistsAsync(string key, CancellationToken ct = default) => db.LessonSignals.AnyAsync(x => x.Key == key, ct);

    public void AddSignal(GovernanceLessonSignal signal) => db.LessonSignals.Add(signal);

    public void AddEvents(IEnumerable<GovernanceEvent> events) => db.Events.AddRange(events);

    public async Task<(int Total, List<GovernanceEvent> Items)> QueryEventsAsync(GovernanceEventFilter f, CancellationToken ct = default)
    {
        var q = db.Events.AsNoTracking();
        if (f.From is { } from) q = q.Where(e => e.StartedAt >= from);
        if (f.To is { } to) q = q.Where(e => e.StartedAt < to);
        if (f.UserId is { } uid) q = q.Where(e => e.UserId == uid);
        if (!string.IsNullOrWhiteSpace(f.User))
        {
            var u = f.User.Trim().ToLower();
            q = q.Where(e => e.UserName.ToLower().Contains(u));
        }
        if (f.Card is { } card) q = q.Where(e => e.CardNumber == card);
        if (!string.IsNullOrWhiteSpace(f.Source)) q = q.Where(e => e.Source == f.Source);
        if (!string.IsNullOrWhiteSpace(f.Category)) q = q.Where(e => e.Category == f.Category);
        if (!string.IsNullOrWhiteSpace(f.Outcome)) q = q.Where(e => e.Outcome == f.Outcome);
        if (!string.IsNullOrWhiteSpace(f.Action))
        {
            var a = f.Action.Trim().ToLower();
            q = q.Where(e => e.Action.ToLower().Contains(a));
        }
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim().ToLower();
            q = q.Where(e => e.Action.ToLower().Contains(s) || (e.Detail != null && e.Detail.ToLower().Contains(s))
                             || e.UserName.ToLower().Contains(s) || e.Rules.Contains(s));
        }

        var size = Math.Clamp(f.Size, 1, 200);
        var page = Math.Max(1, f.Page);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(e => e.StartedAt).ThenByDescending(e => e.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return (total, items);
    }

    public Task<int> PurgeEventsAsync(DateTimeOffset before, CancellationToken ct = default) =>
        db.Events.Where(e => e.StartedAt < before).ExecuteDeleteAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
