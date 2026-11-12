using solvace.executionplans.domain.Responses;
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

    // 0080: as consultas "do card" são da pista principal (análise/correção) — o especialista tem as dele.
    private const string Specialist = ExecutionRequestKind.Specialist;
    /// <summary>0085: kinds fora da pista principal (especialista e itens de trabalho) — não contam no "um pedido ativo por card".</summary>
    private static readonly string[] SideKinds = [ExecutionRequestKind.Specialist, ExecutionRequestKind.WorkItem, ExecutionRequestKind.WorkItemUndo];
    private static readonly string[] WorkItemKinds = [ExecutionRequestKind.WorkItem, ExecutionRequestKind.WorkItemUndo];

    public Task<ExecutionRequest?> GetActiveRequestForCardAsync(string cardNumber, CancellationToken cancellationToken) =>
        _context.Requests.FirstOrDefaultAsync(r => r.CardNumber == cardNumber && !SideKinds.Contains(r.Kind) && Active.Contains(r.Status), cancellationToken);

    public Task<ExecutionRequest?> GetActiveSpecialistForCardAsync(string cardNumber, CancellationToken cancellationToken) =>
        _context.Requests.FirstOrDefaultAsync(r => r.CardNumber == cardNumber && r.Kind == Specialist && Active.Contains(r.Status), cancellationToken);

    public Task<ExecutionRequest?> GetLastSpecialistForCardAsync(string cardNumber, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .Where(r => r.CardNumber == cardNumber && r.Kind == Specialist)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<CaseUsageResponse> GetSpecialistUsageAsync(string cardNumber, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var query = _context.Requests.AsNoTracking().Where(r => r.CardNumber == cardNumber && r.Kind == Specialist);
        if (since is { } s) query = query.Where(r => r.CreatedAt > s);
        var sum = await query.GroupBy(_ => 1).Select(g => new
        {
            Responses = g.Count(r => r.InputTokens != null),
            Input = g.Sum(r => r.InputTokens ?? 0),
            Output = g.Sum(r => r.OutputTokens ?? 0),
            Fresh = g.Sum(r => r.FreshInputTokens ?? 0),
            CacheRead = g.Sum(r => r.CacheReadTokens ?? 0),
            CacheWrite = g.Sum(r => r.CacheWriteTokens ?? 0),
            Cost = g.Sum(r => r.CostUsd ?? 0)
        }).FirstOrDefaultAsync(cancellationToken);
        return new CaseUsageResponse
        {
            Since = since,
            Responses = sum?.Responses ?? 0,
            InputTokens = sum?.Input ?? 0,
            OutputTokens = sum?.Output ?? 0,
            FreshInputTokens = sum?.Fresh ?? 0,
            CacheReadTokens = sum?.CacheRead ?? 0,
            CacheWriteTokens = sum?.CacheWrite ?? 0,
            CostUsd = sum?.Cost ?? 0
        };
    }

    public Task<List<ExecutionRequest>> GetActiveRequestsForCardsAsync(IReadOnlyCollection<string> cardNumbers, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .Where(r => cardNumbers.Contains(r.CardNumber) && !SideKinds.Contains(r.Kind) && Active.Contains(r.Status))
            .ToListAsync(cancellationToken);

    public Task<List<ExecutionRequest>> GetRecentRequestsForCardAsync(string cardNumber, int limit, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .Where(r => r.CardNumber == cardNumber && !SideKinds.Contains(r.Kind))
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
            .Where(r => r.OwnerUserId == ownerUserId && r.WorkerId != null && !SideKinds.Contains(r.Kind) && Busy.Contains(r.Status))
            .GroupBy(r => r.WorkerId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken))
        .ToDictionary(x => x.Key, x => x.Count);

    public async Task<Dictionary<Guid, int>> CountActiveSpecialistsByWorkerAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        (await _context.Requests.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.WorkerId != null && r.Kind == Specialist && Busy.Contains(r.Status))
            .GroupBy(r => r.WorkerId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken))
        .ToDictionary(x => x.Key, x => x.Count);

    public async Task<Dictionary<Guid, int>> CountActiveWorkItemsByWorkerAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        (await _context.Requests.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.WorkerId != null && WorkItemKinds.Contains(r.Kind) && Busy.Contains(r.Status))
            .GroupBy(r => r.WorkerId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken))
        .ToDictionary(x => x.Key, x => x.Count);

    public Task<ExecutionRequest?> GetActiveForWorkItemAsync(Guid workItemId, CancellationToken cancellationToken) =>
        _context.Requests.FirstOrDefaultAsync(r => r.WorkItemId == workItemId && Active.Contains(r.Status), cancellationToken);

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
        _context.Requests.AsNoTracking().AnyAsync(r => r.CardNumber == cardNumber && !SideKinds.Contains(r.Kind) && r.CreatedAt >= since, cancellationToken);

    public Task<DateTimeOffset?> GetLastFinishedAtAsync(string cardNumber, CancellationToken cancellationToken) =>
        _context.Requests.AsNoTracking()
            .Where(r => r.CardNumber == cardNumber && !SideKinds.Contains(r.Kind) && r.FinishedAt != null)
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
                                          && pg.ConstraintName is "IX_ExecutionRequests_ActiveCard" or "IX_ExecutionRequests_ActiveCaseCard"
                                              or "IX_ExecutionRequests_ActiveWorkItem")
        {
            throw new ExecutionRequestDuplicateException(e);
        }
    }

    public void ClearTracking() => _context.ChangeTracker.Clear();

    public async Task ReloadAsync(ExecutionRequest request, CancellationToken cancellationToken) =>
        await _context.Entry(request).ReloadAsync(cancellationToken);

    public void Detach(ExecutionRequest request) => _context.Entry(request).State = EntityState.Detached;
}
