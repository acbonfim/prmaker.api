using Microsoft.EntityFrameworkCore;
using solvace.vacations.application.Contracts;
using solvace.vacations.domain.Entities;
using solvace.vacations.infra.Contexts;

namespace solvace.vacations.infra.Repositories;

public class UserVacationBalanceRepository : IUserVacationBalanceRepository
{
    private readonly VacationContext _context;

    public UserVacationBalanceRepository(VacationContext context)
    {
        _context = context;
    }

    public async Task<UserVacationBalance> CreateAsync(UserVacationBalance balance, CancellationToken cancellationToken)
    {
        await _context.UserVacationBalances.AddAsync(balance, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return balance;
    }

    public async Task<UserVacationBalance?> GetByUserIdAndYearAsync(Guid userId, int year, CancellationToken cancellationToken)
    {
        return await _context.UserVacationBalances
            .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == year, cancellationToken);
    }

    public async Task<UserVacationBalance?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await _context.UserVacationBalances
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public async Task<UserVacationBalance> UpdateAsync(UserVacationBalance balance, CancellationToken cancellationToken)
    {
        _context.UserVacationBalances.Update(balance);
        await _context.SaveChangesAsync(cancellationToken);
        return balance;
    }

    public async Task<List<UserVacationBalance>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.UserVacationBalances
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.AcquisitionPeriodStart)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserVacationBalance?> GetByUserIdAndAcquisitionPeriodAsync(
        Guid userId,
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken cancellationToken)
    {
        return await _context.UserVacationBalances
            .FirstOrDefaultAsync(b =>
                b.UserId == userId &&
                b.AcquisitionPeriodStart == periodStart.Date &&
                b.AcquisitionPeriodEnd == periodEnd.Date,
                cancellationToken);
    }

    public async Task<UserVacationBalance?> GetActiveBalanceForDateAsync(
        Guid userId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var targetDate = date.Date;
        return await _context.UserVacationBalances
            .Where(b =>
                b.UserId == userId &&
                targetDate >= b.UsagePeriodStart &&
                targetDate <= b.UsagePeriodEnd)
            .OrderByDescending(b => b.AcquisitionPeriodStart)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<UserVacationBalance>> GetAllAsync(CancellationToken cancellationToken, IEnumerable<Guid>? userIds = null)
    {
        var query = _context.UserVacationBalances.AsQueryable();

        if (userIds != null)
        {
            var ids = userIds.ToList();
            query = query.Where(b => ids.Contains(b.UserId));
        }

        return await query
            .OrderByDescending(b => b.AcquisitionPeriodStart)
            .ToListAsync(cancellationToken);
    }
}
