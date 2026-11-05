using Microsoft.EntityFrameworkCore;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
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

    public Task<List<WorkItem>> GetByPlanAsync(Guid planId, CancellationToken cancellationToken) =>
        context.WorkItems.Include(i => i.Tasks)
            .Where(i => i.PlanId == planId)
            .OrderBy(i => i.Number)
            .ToListAsync(cancellationToken);

    public Task<WorkItem?> FindDuplicateAsync(string cardNumber, string fingerprint, Guid? exceptId, CancellationToken cancellationToken) =>
        context.WorkItems.AsNoTracking()
            .Where(i => i.CardNumber == cardNumber && i.Fingerprint == fingerprint
                        && i.Status != WorkItemStatus.Cancelled && i.Status != WorkItemStatus.Reverted
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
