using Microsoft.EntityFrameworkCore;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.infra.Contexts;

namespace solvace.executionplans.infra.Repositories;

/// <summary>0099 (ajustes 2): histórias e bugs dos épicos (schema <c>execution</c>).</summary>
public class EpicStoryRepository(ExecutionPlanContext context) : IEpicStoryRepository
{
    public void Add(EpicStory story) => context.EpicStories.Add(story);

    public Task<EpicStory?> GetByNumberAsync(long number, CancellationToken ct) =>
        context.EpicStories.FirstOrDefaultAsync(s => s.Number == number, ct);

    public Task<EpicStory?> FindByNumberAsync(long number, CancellationToken ct) =>
        context.EpicStories.AsNoTracking().FirstOrDefaultAsync(s => s.Number == number, ct);

    public Task<List<EpicStory>> ListByEpicAsync(Guid epicId, IReadOnlyCollection<string>? statuses, CancellationToken ct)
    {
        var q = context.EpicStories.AsNoTracking().Where(s => s.EpicId == epicId);
        if (statuses is { Count: > 0 })
            q = q.Where(s => statuses.Contains(s.Status));
        return q.OrderByDescending(s => s.Number).ToListAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await AssignNumbersAsync(ct);
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ExecutionPlanConcurrencyException(e);
        }
    }

    /// <summary>Número da sequência global para as histórias novas (o mesmo <c>nextval</c> de épicos e itens), em ordem de criação.</summary>
    private async Task AssignNumbersAsync(CancellationToken ct)
    {
        var pending = context.ChangeTracker.Entries<EpicStory>()
            .Where(e => e.State == EntityState.Added && e.Entity.Number <= 0)
            .OrderBy(e => e.Entity.CreatedAt)
            .ToList();
        if (pending.Count == 0) return;
        var numbers = (await context.Database
                .SqlQueryRaw<long>($"SELECT nextval('{ExecutionPlanContext.Schema}.{ExecutionPlanContext.WorkItemNumberSequence}') AS \"Value\" FROM generate_series(1, {pending.Count})")
                .ToListAsync(ct))
            .Order().ToList();
        for (var i = 0; i < pending.Count; i++) pending[i].Entity.AssignNumber(numbers[i]);
    }

    public void Detach()
    {
        foreach (var entry in context.ChangeTracker.Entries<EpicStory>().ToList())
            entry.State = EntityState.Detached;
    }
}
