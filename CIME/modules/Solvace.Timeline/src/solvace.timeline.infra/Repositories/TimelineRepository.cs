using Microsoft.EntityFrameworkCore;
using solvace.timeline.application.Contracts;
using solvace.timeline.domain.Entities;
using solvace.timeline.infra.Contexts;

namespace solvace.timeline.infra.Repositories;

public class TimelineRepository : ITimelineRepository
{
    private readonly TimelineContext _context;

    public TimelineRepository(TimelineContext context)
    {
        _context = context;
    }

    public async Task<TimelineEntry> CreateAsync(TimelineEntry entry, CancellationToken cancellationToken)
    {
        await _context.TimelineEntries.AddAsync(entry, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<TimelineEntry?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await _context.TimelineEntries
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<List<TimelineEntry>> GetByCardNumberAsync(string cardNumber, CancellationToken cancellationToken)
    {
        return await _context.TimelineEntries
            .Where(e => e.CardNumber == cardNumber)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<TimelineEntry> UpdateAsync(TimelineEntry entry, CancellationToken cancellationToken)
    {
        _context.TimelineEntries.Update(entry);
        await _context.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task DeleteAsync(TimelineEntry entry, CancellationToken cancellationToken)
    {
        _context.TimelineEntries.Remove(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
