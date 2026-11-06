using Microsoft.EntityFrameworkCore;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.executionplans.infra.Contexts;

namespace solvace.executionplans.infra.Repositories;

/// <summary>0085: itens de trabalho do card (schema execution).</summary>
public class WorkItemRepository(ExecutionPlanContext context) : IWorkItemRepository
{
    public void Add(WorkItem item) => context.WorkItems.Add(item);

    public Task<List<WorkItem>> ListByCardAsync(string cardNumber, CancellationToken cancellationToken) =>
        context.WorkItems.AsNoTracking()
            .Include(i => i.Tasks)
            .Where(i => i.CardNumber == cardNumber)
            .OrderBy(i => i.Number)
            .ToListAsync(cancellationToken);

    public Task<WorkItem?> GetByNumberAsync(long number, CancellationToken cancellationToken) =>
        context.WorkItems.Include(i => i.Tasks).FirstOrDefaultAsync(i => i.Number == number, cancellationToken);

    public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.WorkItems.Include(i => i.Tasks).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<WorkItem?> GetByTaskNumberAsync(long taskNumber, CancellationToken cancellationToken) =>
        context.WorkItems.Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => context.WorkItemTasks.Any(t => t.WorkItemId == i.Id && t.Number == taskNumber), cancellationToken);

    public async Task<List<WorkItemSearchHit>> SearchAsync(string? query, string? exceptCard, int limit, CancellationToken cancellationToken)
    {
        var q = (query ?? string.Empty).Trim();
        var digits = q.Length > 0 && q.All(char.IsAsciiDigit);
        var lower = q.ToLowerInvariant();
        var items = context.WorkItems.AsNoTracking().Where(i => exceptCard == null || i.CardNumber != exceptCard);

        var itemHits = items
            .Where(i => q.Length == 0
                        || (digits ? i.Number.ToString().StartsWith(q) || i.CardNumber == q : i.Title.ToLower().Contains(lower)))
            .OrderByDescending(i => digits && i.Number.ToString() == q)
            .ThenByDescending(i => i.Number)
            .Take(limit)
            .Select(i => new WorkItemSearchHit
            {
                Kind = "item", Number = i.Number, Title = i.Title, Status = i.Status, CardNumber = i.CardNumber,
                ItemNumber = i.Number, ItemTitle = i.Title, ItemType = i.Type, UpdatedAt = i.UpdatedAt
            });
        var taskHits = context.WorkItemTasks.AsNoTracking()
            .Join(items, t => t.WorkItemId, i => i.Id, (t, i) => new { t, i })
            .Where(x => q.Length == 0
                        || (digits ? x.t.Number.ToString().StartsWith(q) : x.t.Title.ToLower().Contains(lower)))
            .OrderByDescending(x => digits && x.t.Number.ToString() == q)
            .ThenByDescending(x => x.t.Number)
            .Take(limit)
            .Select(x => new WorkItemSearchHit
            {
                Kind = "task", Number = x.t.Number, Title = x.t.Title, Status = x.t.Status, CardNumber = x.i.CardNumber,
                ItemNumber = x.i.Number, ItemTitle = x.i.Title, ItemType = x.i.Type, UpdatedAt = x.i.UpdatedAt
            });

        var list = await itemHits.ToListAsync(cancellationToken);
        list.AddRange(await taskHits.ToListAsync(cancellationToken));
        // Número exato primeiro; depois os mais recentes (número maior).
        return list
            .OrderByDescending(h => digits && h.Number.ToString() == q)
            .ThenByDescending(h => h.Number)
            .Take(limit)
            .ToList();
    }

    public Task<List<WorkItem>> GetByPlanAsync(Guid planId, CancellationToken cancellationToken) =>
        context.WorkItems.Include(i => i.Tasks)
            .Where(i => i.PlanId == planId)
            .OrderBy(i => i.Number)
            .ToListAsync(cancellationToken);

    public Task<WorkItem?> FindDuplicateAsync(string cardNumber, string fingerprint, Guid? exceptId, CancellationToken cancellationToken) =>
        context.WorkItems.AsNoTracking()
            .Where(i => i.CardNumber == cardNumber && i.Fingerprint == fingerprint
                        && i.Status != WorkItemStatus.Cancelled && i.Status != WorkItemStatus.Reverted
                        // Ajustes 0085: o item da análise (espelho do plano de análise) não é entrega — não barra item novo.
                        && !(i.Origin == WorkItemOrigin.Analysis && i.PlanId != null)
                        && (exceptId == null || i.Id != exceptId))
            .OrderBy(i => i.Number)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<List<WorkItemLog>> GetLogsAsync(Guid workItemId, int limit, CancellationToken cancellationToken)
    {
        var logs = await context.WorkItemLogs.AsNoTracking()
            .Where(l => l.WorkItemId == workItemId)
            .OrderByDescending(l => l.Id)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(cancellationToken);
        logs.Reverse();
        return logs;
    }

    private static readonly string[] ActiveStatuses = [ExecutionRequestStatus.Queued, ExecutionRequestStatus.Claimed, ExecutionRequestStatus.Running];
    private static readonly string[] MainKinds = [ExecutionRequestKind.Analyze, ExecutionRequestKind.Resume];

    public async Task<(Dictionary<Guid, (Guid RequestId, string? Activity)> ByItem, Dictionary<Guid, (Guid RequestId, string? Activity)> ByPlan)>
        GetLiveRequestsAsync(IReadOnlyCollection<Guid> workItemIds, IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken)
    {
        var byItem = new Dictionary<Guid, (Guid, string?)>();
        var byPlan = new Dictionary<Guid, (Guid, string?)>();
        if (workItemIds.Count == 0 && planIds.Count == 0) return (byItem, byPlan);
        var rows = await context.Requests.AsNoTracking()
            .Where(r => ActiveStatuses.Contains(r.Status)
                        && ((r.WorkItemId != null && workItemIds.Contains(r.WorkItemId.Value))
                            || (r.WorkItemId == null && r.PlanId != null && planIds.Contains(r.PlanId.Value) && MainKinds.Contains(r.Kind))))
            .Select(r => new { r.Id, r.WorkItemId, r.PlanId, r.CurrentActivity, r.CreatedAt })
            .ToListAsync(cancellationToken);
        foreach (var r in rows.OrderBy(r => r.CreatedAt))
        {
            if (r.WorkItemId is { } w) byItem[w] = (r.Id, r.CurrentActivity);
            else if (r.PlanId is { } p) byPlan[p] = (r.Id, r.CurrentActivity);
        }
        return (byItem, byPlan);
    }

    public async Task<Dictionary<Guid, string>> GetActivitiesAsync(IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken)
    {
        if (requestIds.Count == 0) return [];
        var rows = await context.Requests.AsNoTracking()
            .Where(r => requestIds.Contains(r.Id) && r.CurrentActivity != null)
            .Select(r => new { r.Id, r.CurrentActivity })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.CurrentActivity!);
    }

    public async Task<WorkItemLiveHead?> GetLiveHeadAsync(long number, CancellationToken cancellationToken)
    {
        var row = await context.WorkItems.AsNoTracking()
            .Where(i => i.Number == number)
            .Select(i => new
            {
                i.Id, i.Number, i.CardNumber, i.Status, i.PlanId, i.CreatedBy,
                Plan = context.Plans.Where(p => p.Id == i.PlanId)
                    .Select(p => new { p.CreatedByUserId, p.Status, p.LastActivityAt })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new WorkItemLiveHead(row.Id, row.Number, row.CardNumber, row.Status, row.PlanId, row.CreatedBy,
                row.Plan?.CreatedByUserId, row.Plan?.Status, row.Plan?.LastActivityAt);
    }

    public async Task<WorkItemLiveRequestRow?> GetLiveRequestAsync(Guid workItemId, string cardNumber, Guid? planId,
        CancellationToken cancellationToken)
    {
        var query = context.Requests.AsNoTracking().Where(r => ActiveStatuses.Contains(r.Status));
        query = planId is { } pid
            ? query.Where(r => r.WorkItemId == workItemId
                               || (r.WorkItemId == null && r.CardNumber == cardNumber && MainKinds.Contains(r.Kind)
                                   && (r.PlanId == pid || r.PlanId == null)))
            : query.Where(r => r.WorkItemId == workItemId);
        var rows = await query
            .Select(r => new WorkItemLiveRequestRow(r.Id, r.Kind, r.Status, r.OwnerUserId, r.WorkerId, r.WorkerName, r.ClaimedAt,
                r.StartedAt, r.LastHeartbeatAt, r.CurrentActivity, r.CurrentActivityTool, r.CurrentActivityAt, r.RecentActivities))
            .ToListAsync(cancellationToken);
        // O pedido do próprio item vale mais que o do plano.
        return rows.Where(r => ExecutionRequestKind.IsWorkItem(r.Kind)).FirstOrDefault() ?? rows.FirstOrDefault();
    }

    public async Task<List<(string CardNumber, long Number, string Status)>> GetPlanItemHeadsAsync(Guid planId, CancellationToken cancellationToken) =>
        (await context.WorkItems.AsNoTracking()
            .Where(i => i.PlanId == planId)
            .OrderBy(i => i.Number)
            .Select(i => new { i.CardNumber, i.Number, i.Status })
            .ToListAsync(cancellationToken))
        .Select(i => (i.CardNumber, i.Number, i.Status)).ToList();

    public Task<List<WorkItemLiveWorker>> GetLiveWorkersAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        context.Workers.AsNoTracking()
            .Where(w => w.OwnerUserId == ownerUserId && w.Status != ExecutionWorkerStatus.Revoked)
            .Select(w => new WorkItemLiveWorker(w.Id, w.Status, w.LastSeenAt))
            .ToListAsync(cancellationToken);

    public async Task<Dictionary<Guid, string>> GetPlanPhasesAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken)
    {
        if (planIds.Count == 0) return [];
        var rows = await context.Plans.AsNoTracking()
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Phase })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.Phase);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await AssignNumbersAsync(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ExecutionPlanConcurrencyException(e);
        }
    }

    /// <summary>
    /// Números novos em ordem de leitura: o item e logo depois as sub-tarefas dele na ordem (#1, #2, #3…). Deixar para o
    /// default do banco numerava na ordem de insert do EF (sub-tarefas ao contrário).
    /// </summary>
    private async Task AssignNumbersAsync(CancellationToken cancellationToken)
    {
        bool Pending(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry e) =>
            e.State == EntityState.Added && (long)(e.Property("Number").CurrentValue ?? 0L) <= 0;

        var tasks = context.ChangeTracker.Entries<WorkItemTask>().Where(Pending).ToList();
        var ordered = new List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>();
        foreach (var item in context.ChangeTracker.Entries<WorkItem>().Where(Pending))
        {
            ordered.Add(item);
            ordered.AddRange(tasks.Where(t => t.Entity.WorkItemId == item.Entity.Id).OrderBy(t => t.Entity.Order));
        }
        ordered.AddRange(tasks.Where(t => !ordered.Contains(t)).OrderBy(t => t.Entity.WorkItemId).ThenBy(t => t.Entity.Order));
        if (ordered.Count == 0) return;

        var numbers = (await context.Database
                .SqlQueryRaw<long>($"SELECT nextval('{ExecutionPlanContext.Schema}.{ExecutionPlanContext.WorkItemNumberSequence}') AS \"Value\" FROM generate_series(1, {ordered.Count})")
                .ToListAsync(cancellationToken))
            .Order().ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Property("Number").CurrentValue = numbers[i];
    }

    public void DetachWorkItems()
    {
        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(e => e.Entity is WorkItem or WorkItemTask or WorkItemLog).ToList())
            entry.State = EntityState.Detached;
    }
}
