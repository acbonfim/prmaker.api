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
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ExecutionPlanConcurrencyException(e);
        }
    }

    public void DetachWorkItems()
    {
        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(e => e.Entity is WorkItem or WorkItemTask or WorkItemLog).ToList())
            entry.State = EntityState.Detached;
    }
}
