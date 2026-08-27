using Microsoft.EntityFrameworkCore;
using solvace.vacations.application.Contracts;
using solvace.vacations.domain.Entities;
using solvace.vacations.infra.Contexts;

namespace solvace.vacations.infra.Repositories;

public class VacationRepository : IVacationRepository
{
    private readonly VacationContext _context;

    public VacationRepository(VacationContext context)
    {
        _context = context;
    }

    public async Task<VacationRequest> CreateAsync(VacationRequest request, CancellationToken cancellationToken)
    {
        await _context.VacationRequests.AddAsync(request, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task<VacationRequest?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await _context.VacationRequests
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<List<VacationRequest>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.VacationRequests
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<VacationRequest>> GetAllAsync(CancellationToken cancellationToken, IEnumerable<Guid>? userIds = null)
    {
        var query = _context.VacationRequests.AsQueryable();

        if (userIds != null)
        {
            var ids = userIds.ToList();
            query = query.Where(v => ids.Contains(v.UserId));
        }

        return await query
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<VacationRequest>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken, IEnumerable<Guid>? userIds = null)
    {
        var query = _context.VacationRequests
            .Where(v => v.StartDate <= endDate && v.EndDate >= startDate);

        if (userIds != null)
        {
            var ids = userIds.ToList();
            query = query.Where(v => ids.Contains(v.UserId));
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<VacationRequest> UpdateAsync(VacationRequest request, CancellationToken cancellationToken)
    {
        _context.VacationRequests.Update(request);
        await _context.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var vacation = await GetByIdAsync(id, cancellationToken);
        if (vacation != null)
        {
            _context.VacationRequests.Remove(vacation);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> HasConflictingDatesAsync(
        Guid userId,
        DateTime startDate,
        DateTime endDate,
        int? excludeRequestId,
        CancellationToken cancellationToken)
    {
        var query = _context.VacationRequests
            .Where(v => v.UserId == userId &&
                        v.Status != domain.Enums.VacationStatus.Cancelled &&
                        (v.StartDate <= endDate && v.EndDate >= startDate));

        if (excludeRequestId.HasValue)
        {
            query = query.Where(v => v.Id != excludeRequestId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }
}
