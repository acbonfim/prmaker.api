using Microsoft.EntityFrameworkCore;
using Npgsql;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.infra.Contexts;

namespace solvace.executionplans.infra.Repositories;

public class ExecutionQueueRepository : IExecutionQueueRepository
{
    private static readonly string[] Busy = [ExecutionRequestStatus.Claimed, ExecutionRequestStatus.Running];
    private static readonly string[] Active = [ExecutionRequestStatus.Queued, ExecutionRequestStatus.Claimed, ExecutionRequestStatus.Running];

    private readonly ExecutionPlanContext _context;

    public ExecutionQueueRepository(ExecutionPlanContext context)
    {
        _context = context;
    }

    public void AddRequest(ExecutionRequest request) => _context.Requests.Add(request);

    public Task<ExecutionRequest?> GetRequestAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Requests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<ExecutionRequest?> GetActiveRequestForCardAsync(string cardNumber, CancellationToken cancellationToken) =>
        _context.Requests.FirstOrDefaultAsync(r => r.CardNumber == cardNumber && Active.Contains(r.Status), cancellationToken);

    public Task<List<ExecutionRequest>> GetActiveRequestsForCardsAsync(IReadOnlyCollection<string> cardNumbers, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .Where(r => cardNumbers.Contains(r.CardNumber) && Active.Contains(r.Status))
            .ToListAsync(cancellationToken);

    public Task<List<ExecutionRequest>> GetRecentRequestsForCardAsync(string cardNumber, int limit, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .Where(r => r.CardNumber == cardNumber)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<List<ExecutionRequest>> GetQueuedForOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        _context.Requests
            .Where(r => r.OwnerUserId == ownerUserId && r.Status == ExecutionRequestStatus.Queued)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<List<ExecutionRequest>> GetStaleForOwnerAsync(Guid ownerUserId, DateTimeOffset now, DateTimeOffset queuedBefore, CancellationToken cancellationToken) =>
        _context.Requests
            .Where(r => r.OwnerUserId == ownerUserId
                        && ((Busy.Contains(r.Status) && r.LeaseUntil != null && r.LeaseUntil < now)
                            || (r.Status == ExecutionRequestStatus.Queued && r.CreatedAt < queuedBefore)))
            .ToListAsync(cancellationToken);

    public Task<List<ExecutionRequest>> GetActiveForWorkerAsync(Guid workerId, CancellationToken cancellationToken) =>
        _context.Requests
            .Where(r => r.WorkerId == workerId && Busy.Contains(r.Status))
            .ToListAsync(cancellationToken);

    public async Task<Dictionary<Guid, int>> CountActiveByWorkerAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        (await _context.Requests.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.WorkerId != null && Busy.Contains(r.Status))
            .GroupBy(r => r.WorkerId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken))
        .ToDictionary(x => x.Key, x => x.Count);

    public async Task<decimal> GetCostSinceAsync(Guid ownerUserId, DateTimeOffset since, CancellationToken cancellationToken) =>
        await _context.Requests.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.CostUsd != null && (r.FinishedAt ?? r.UpdatedAt) >= since)
            .SumAsync(r => r.CostUsd ?? 0, cancellationToken);

    public Task<decimal?> GetLastSessionCostAsync(string sessionId, Guid exceptRequestId, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .Where(r => r.SessionId == sessionId && r.Id != exceptRequestId && r.SessionCostUsd != null)
            .MaxAsync(r => r.SessionCostUsd, cancellationToken);

    public Task<ExecutionRequest?> GetSessionStarterAsync(string sessionId, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .Where(r => r.SessionId == sessionId && r.StartedAt != null)
            .OrderBy(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountBySourceSinceAsync(Guid ownerUserId, string source, DateTimeOffset since, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .CountAsync(r => r.OwnerUserId == ownerUserId && r.Source == source && r.CreatedAt >= since, cancellationToken);

    public Task<bool> HasRequestForCardSinceAsync(string cardNumber, DateTimeOffset since, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking().AnyAsync(r => r.CardNumber == cardNumber && r.CreatedAt >= since, cancellationToken);

    public Task<DateTimeOffset?> GetLastFinishedAtAsync(string cardNumber, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .Where(r => r.CardNumber == cardNumber && r.FinishedAt != null)
            .MaxAsync(r => r.FinishedAt, cancellationToken);

    public Task<List<ExecutionRequest>> GetHistoryForOwnerAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void AddWorker(ExecutionWorker worker) => _context.Workers.Add(worker);

    public Task<ExecutionWorker?> GetWorkerAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Workers.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<List<ExecutionWorker>> GetWorkersByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        _context.Workers
            .Where(w => w.OwnerUserId == ownerUserId)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<ExecutionWorker?> FindWorkerByHostAsync(Guid ownerUserId, string host, CancellationToken cancellationToken) =>
        _context.Workers
            .Where(w => w.OwnerUserId == ownerUserId && w.Host.ToLower() == host.ToLower())
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<(string Status, Guid CredentialId)?> GetWorkerCredentialAsync(Guid workerId, CancellationToken cancellationToken)
    {
        var row = await _context.Workers.AsNoTracking()
            .Where(w => w.Id == workerId)
            .Select(w => new { w.Status, w.CredentialId })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Status, row.CredentialId);
    }

    public async Task<HashSet<Guid>> GetOwnersWithWorkersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        (await _context.Workers.AsNoTracking()
            .Where(w => userIds.Contains(w.OwnerUserId) && w.Status != ExecutionWorkerStatus.Revoked)
            .Select(w => w.OwnerUserId)
            .Distinct()
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public Task<ExecutionUserSettings?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken) =>
        _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public void AddSettings(ExecutionUserSettings settings) => _context.UserSettings.Add(settings);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ExecutionPlanConcurrencyException(e);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                          && pg.ConstraintName == "IX_ExecutionRequests_ActiveCard")
        {
            throw new ExecutionRequestDuplicateException(e);
        }
    }

    public void ClearTracking() => _context.ChangeTracker.Clear();
}
