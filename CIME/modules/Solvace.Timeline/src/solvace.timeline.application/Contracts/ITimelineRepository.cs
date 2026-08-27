using solvace.timeline.domain.Entities;

namespace solvace.timeline.application.Contracts;

public interface ITimelineRepository
{
    Task<TimelineEntry> CreateAsync(TimelineEntry entry, CancellationToken cancellationToken);
    Task<TimelineEntry?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<List<TimelineEntry>> GetByCardNumberAsync(string cardNumber, CancellationToken cancellationToken);
    Task<TimelineEntry> UpdateAsync(TimelineEntry entry, CancellationToken cancellationToken);
    Task DeleteAsync(TimelineEntry entry, CancellationToken cancellationToken);
}
