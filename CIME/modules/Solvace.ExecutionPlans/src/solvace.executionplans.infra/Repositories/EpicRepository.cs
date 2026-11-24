using Microsoft.EntityFrameworkCore;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.executionplans.infra.Contexts;

namespace solvace.executionplans.infra.Repositories;

/// <summary>0099: épicos da Mesa de gestão (schema <c>execution</c>).</summary>
public class EpicRepository(ExecutionPlanContext context) : IEpicRepository
{
    public void Add(Epic epic) => context.Epics.Add(epic);

    public Task<Epic?> GetByNumberAsync(long number, CancellationToken ct) =>
        context.Epics.Include(e => e.Cards).FirstOrDefaultAsync(e => e.Number == number, ct);

    public async Task<List<Epic>> ListAsync(IReadOnlyCollection<string>? statuses, string? query, Guid? createdBy, int limit, CancellationToken ct)
    {
        var q = context.Epics.AsNoTracking();
        if (statuses is { Count: > 0 })
            q = q.Where(e => statuses.Contains(e.Status));
        if (createdBy is { } me)
            q = q.Where(e => e.CreatedBy == me);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim().TrimStart('#');
            if (EpicKey.TryParse(text, out var byKey))
                q = q.Where(e => e.Number == byKey);
            else if (long.TryParse(text, out var n))
            {
                var digits = text;
                q = q.Where(e => e.Number == n || e.Number.ToString().StartsWith(digits) || e.DevOpsCardId == digits);
            }
            else
            {
                // Texto: título ou tag. Tags são jsonb (lower() não se aplica): projeção leve e filtro em memória — épicos são
                // poucos; depois só os que casaram, inteiros.
                var lower = text.ToLowerInvariant();
                var heads = await q.Select(e => new { e.Id, e.Title, e.TagsJson }).ToListAsync(ct);
                var ids = heads.Where(h => h.Title.Contains(lower, StringComparison.OrdinalIgnoreCase)
                                           || h.TagsJson.Contains(lower, StringComparison.OrdinalIgnoreCase))
                    .Select(h => h.Id).ToList();
                q = context.Epics.AsNoTracking().Where(e => ids.Contains(e.Id));
            }
        }
        return await q.OrderByDescending(e => e.UpdatedAt).ThenByDescending(e => e.Number).Take(Math.Clamp(limit, 1, 1000)).ToListAsync(ct);
    }

    public Task<List<Epic>> ListByNumbersAsync(IReadOnlyCollection<long> numbers, CancellationToken ct) =>
        context.Epics.AsNoTracking().Where(e => numbers.Contains(e.Number)).OrderBy(e => e.Number).ToListAsync(ct);

    public Task<List<Epic>> GetByNumbersAsync(IReadOnlyCollection<long> numbers, CancellationToken ct) =>
        context.Epics.Include(e => e.Cards).Where(e => numbers.Contains(e.Number)).OrderBy(e => e.Number).ToListAsync(ct);

    public Task<List<string>> ListTagsJsonAsync(CancellationToken ct) =>
        context.Epics.AsNoTracking().Select(e => e.TagsJson).ToListAsync(ct);

    public Task<List<Epic>> ListByCardAsync(string card, CancellationToken ct) =>
        context.Epics.AsNoTracking()
            .Where(e => context.EpicCards.Any(c => c.EpicId == e.Id && c.CardNumber == card))
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, EpicCounts>> CountsAsync(IReadOnlyCollection<Epic> epics, CancellationToken ct)
    {
        var result = epics.ToDictionary(e => e.Id, _ => new EpicCounts());
        if (result.Count == 0) return result;
        var ids = result.Keys.ToList();
        var cards = await context.EpicCards.AsNoTracking()
            .Where(c => ids.Contains(c.EpicId))
            .GroupBy(c => c.EpicId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        foreach (var c in cards) result[c.Key].Cards = c.Count;

        var keys = epics.ToDictionary(e => EpicKey.Of(e.Number), e => e.Id);
        var keyList = keys.Keys.ToList();
        string[] closed = [WorkItemStatus.Completed, WorkItemStatus.Cancelled, WorkItemStatus.Reverted];
        var items = await context.WorkItems.AsNoTracking()
            .Where(i => keyList.Contains(i.CardNumber))
            .GroupBy(i => i.CardNumber)
            .Select(g => new { g.Key, Total = g.Count(), Open = g.Count(i => !closed.Contains(i.Status)) })
            .ToListAsync(ct);
        foreach (var i in items)
            if (keys.TryGetValue(i.Key, out var id))
            {
                result[id].Items = i.Total;
                result[id].OpenItems = i.Open;
            }
        return result;
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
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // O mesmo card entrou no épico ao mesmo tempo por outro caminho — quem chama tenta de novo.
            throw new ExecutionPlanConcurrencyException(e);
        }
    }

    /// <summary>Número da sequência global para os épicos novos (o mesmo <c>nextval</c> dos itens), em ordem de criação.</summary>
    private async Task AssignNumbersAsync(CancellationToken ct)
    {
        var pending = context.ChangeTracker.Entries<Epic>()
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
        foreach (var entry in context.ChangeTracker.Entries().Where(e => e.Entity is Epic or EpicCard).ToList())
            entry.State = EntityState.Detached;
    }
}
