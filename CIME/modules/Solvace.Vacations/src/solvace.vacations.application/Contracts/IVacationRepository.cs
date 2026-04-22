using solvace.vacations.domain.Entities;

namespace solvace.vacations.application.Contracts;

public interface IVacationRepository
{
    Task<VacationRequest> CreateAsync(VacationRequest request, CancellationToken cancellationToken);
    Task<VacationRequest?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<List<VacationRequest>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<List<VacationRequest>> GetAllAsync(CancellationToken cancellationToken);
    Task<List<VacationRequest>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken);
    Task<VacationRequest> UpdateAsync(VacationRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
    Task<bool> HasConflictingDatesAsync(Guid userId, DateTime startDate, DateTime endDate, int? excludeRequestId, CancellationToken cancellationToken);
}
