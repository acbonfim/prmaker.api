using Microsoft.EntityFrameworkCore;
using Npgsql;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.infra.Contexts;

namespace solvace.executionplans.infra.Repositories;

/// <summary>0085 r5: arquivos do item de trabalho (schema execution).</summary>
public class WorkItemFileRepository(ExecutionPlanContext context) : IWorkItemFileRepository
{
    private static readonly HashSet<string> ConflictIndexes =
        ["IX_WorkItemFiles_WorkItemId_Number", "IX_WorkItemFiles_WorkItemId_Kind_Name"];

    public Task<WorkItem?> GetItemAsync(long number, CancellationToken ct) =>
        context.WorkItems.AsNoTracking().Include(i => i.Tasks).FirstOrDefaultAsync(i => i.Number == number, ct);

    public Task<List<WorkItemFile>> ListAsync(Guid workItemId, CancellationToken ct) =>
        context.WorkItemFiles.AsNoTracking()
            .Where(f => f.WorkItemId == workItemId)
            .OrderBy(f => f.Number)
            .ToListAsync(ct);

    public Task<WorkItemFile?> GetAsync(Guid workItemId, Guid fileId, CancellationToken ct) =>
        context.WorkItemFiles.AsNoTracking().FirstOrDefaultAsync(f => f.WorkItemId == workItemId && f.Id == fileId, ct);

    public Task<WorkItemFile?> FindForUpdateAsync(Guid workItemId, string kind, string name, CancellationToken ct) =>
        context.WorkItemFiles.Include(f => f.Content)
            .FirstOrDefaultAsync(f => f.WorkItemId == workItemId && f.Kind == kind && f.Name == name, ct);

    public Task<byte[]?> GetContentAsync(Guid fileId, CancellationToken ct) =>
        context.WorkItemFileContents.AsNoTracking().Where(c => c.FileId == fileId).Select(c => c.Data).FirstOrDefaultAsync(ct);

    public async Task<int> GetMaxNumberAsync(Guid workItemId, CancellationToken ct) =>
        await context.WorkItemFiles.AsNoTracking().Where(f => f.WorkItemId == workItemId).MaxAsync(f => (int?)f.Number, ct) ?? 0;

    public Task<long> GetSizeAsync(Guid workItemId, Guid? excludingFileId, CancellationToken ct) =>
        context.WorkItemFiles.AsNoTracking()
            .Where(f => f.WorkItemId == workItemId && (excludingFileId == null || f.Id != excludingFileId))
            .SumAsync(f => f.Size, ct);

    public Task<List<ExecutionArtifact>> GetPlanArtifactsAsync(Guid planId, CancellationToken ct) =>
        context.Artifacts.AsNoTracking()
            .Where(a => a.PlanId == planId && a.NoteId == null && a.ExternalKey == null)
            .OrderBy(a => a.Number).ThenBy(a => a.Name)
            .ToListAsync(ct);

    public Task<List<WorkItem>> GetItemsByCardAsync(string cardNumber, CancellationToken ct) =>
        context.WorkItems.AsNoTracking().Include(i => i.Tasks).Where(i => i.CardNumber == cardNumber).OrderBy(i => i.Number).ToListAsync(ct);

    public Task<List<WorkItemFile>> ListByItemsAsync(IReadOnlyCollection<Guid> workItemIds, CancellationToken ct) =>
        workItemIds.Count == 0
            ? Task.FromResult(new List<WorkItemFile>())
            : context.WorkItemFiles.AsNoTracking().Where(f => workItemIds.Contains(f.WorkItemId)).OrderBy(f => f.Number).ToListAsync(ct);

    public Task<List<ExecutionArtifact>> GetCardPlanArtifactsAsync(string cardNumber, CancellationToken ct) =>
        (from a in context.Artifacts.AsNoTracking()
         join p in context.Plans.AsNoTracking() on a.PlanId equals p.Id
         where p.CardNumber == cardNumber && a.NoteId == null && a.ExternalKey == null
         orderby a.Number, a.Name
         select a).ToListAsync(ct);

    public Task<string?> GetPlanPhaseAsync(Guid planId, CancellationToken ct) =>
        context.Plans.AsNoTracking().Where(p => p.Id == planId).Select(p => (string?)p.Phase).FirstOrDefaultAsync(ct);

    public void Add(WorkItemFile file) => context.WorkItemFiles.Add(file);

    public void AddLog(WorkItemLog log) => context.WorkItemLogs.Add(log);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                          && pg.ConstraintName is { } name && ConflictIndexes.Contains(name))
        {
            throw new WorkItemFileNumberConflictException(e);
        }
    }

    public void ClearTracking() => context.ChangeTracker.Clear();
}
