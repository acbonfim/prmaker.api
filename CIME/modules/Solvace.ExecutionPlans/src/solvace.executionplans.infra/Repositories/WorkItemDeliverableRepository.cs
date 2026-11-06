using Microsoft.EntityFrameworkCore;
using Npgsql;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.infra.Contexts;

namespace solvace.executionplans.infra.Repositories;

/// <summary>0091: entregas do item de trabalho (schema execution).</summary>
public class WorkItemDeliverableRepository(ExecutionPlanContext context) : IWorkItemDeliverableRepository
{
    private const string KeyIndex = "IX_WorkItemDeliverables_CardNumber_TaskNumber_Key";

    public Task<WorkItem?> GetItemAsync(long number, CancellationToken ct) =>
        context.WorkItems.AsNoTracking().Include(i => i.Tasks).FirstOrDefaultAsync(i => i.Number == number, ct);

    public Task<WorkItem?> GetItemByTaskAsync(long taskNumber, CancellationToken ct) =>
        context.WorkItems.AsNoTracking().Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => i.Tasks.Any(t => t.Number == taskNumber), ct);

    public Task<List<WorkItem>> GetItemsByPlanAsync(Guid planId, CancellationToken ct) =>
        context.WorkItems.AsNoTracking().Include(i => i.Tasks).Where(i => i.PlanId == planId).OrderBy(i => i.Number).ToListAsync(ct);

    public Task<List<WorkItem>> GetItemsWithRunningTaskAsync(string cardNumber, CancellationToken ct) =>
        context.WorkItems.AsNoTracking().Include(i => i.Tasks)
            .Where(i => i.CardNumber == cardNumber && i.Tasks.Any(t => t.Status == WorkTaskStatus.Running))
            .ToListAsync(ct);

    public Task<List<WorkItemDeliverable>> ListAsync(Guid workItemId, CancellationToken ct) =>
        context.WorkItemDeliverables.AsNoTracking()
            .Where(d => d.WorkItemId == workItemId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

    public Task<WorkItemDeliverable?> FindForUpdateAsync(string cardNumber, long? taskNumber, string key, CancellationToken ct) =>
        context.WorkItemDeliverables.Include(d => d.Content)
            .FirstOrDefaultAsync(d => d.CardNumber == cardNumber && d.TaskNumber == taskNumber && d.Key == key, ct);

    public Task<WorkItemDeliverable?> GetAsync(Guid workItemId, Guid id, CancellationToken ct) =>
        context.WorkItemDeliverables.AsNoTracking().FirstOrDefaultAsync(d => d.WorkItemId == workItemId && d.Id == id, ct);

    public Task<byte[]?> GetPatchAsync(Guid id, CancellationToken ct) =>
        context.WorkItemDeliverableContents.AsNoTracking().Where(c => c.DeliverableId == id).Select(c => c.Data).FirstOrDefaultAsync(ct);

    public Task<List<ExecutionLink>> GetPlanLinksAsync(Guid planId, CancellationToken ct) =>
        context.Links.AsNoTracking().Where(l => l.PlanId == planId).OrderBy(l => l.CreatedAt).ToListAsync(ct);

    public Task<string?> GetPlanPhaseAsync(Guid planId, CancellationToken ct) =>
        context.Plans.AsNoTracking().Where(p => p.Id == planId).Select(p => (string?)p.Phase).FirstOrDefaultAsync(ct);

    public void Add(WorkItemDeliverable deliverable) => context.WorkItemDeliverables.Add(deliverable);

    public void AddLog(WorkItemLog log) => context.WorkItemLogs.Add(log);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                          && pg.ConstraintName == KeyIndex)
        {
            throw new WorkItemDeliverableConflictException(e);
        }
    }

    public void ClearTracking() => context.ChangeTracker.Clear();
}
