using solvace.vacations.domain.Entities;

namespace solvace.vacations.application.Contracts;

public interface IUserVacationBalanceRepository
{
    Task<UserVacationBalance> CreateAsync(UserVacationBalance balance, CancellationToken cancellationToken);
    Task<UserVacationBalance?> GetByUserIdAndYearAsync(Guid userId, int year, CancellationToken cancellationToken);
    Task<UserVacationBalance?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<UserVacationBalance> UpdateAsync(UserVacationBalance balance, CancellationToken cancellationToken);
    Task<List<UserVacationBalance>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserVacationBalance?> GetByUserIdAndAcquisitionPeriodAsync(Guid userId, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken);
    Task<UserVacationBalance?> GetActiveBalanceForDateAsync(Guid userId, DateTime date, CancellationToken cancellationToken);
}
