using solvace.vacations.domain.Requests;
using solvace.vacations.domain.Responses;

namespace solvace.vacations.application.Contracts;

public interface IVacationApplication
{
    Task<VacationRequestResponse> CreateVacationRequestAsync(CreateVacationRequestRequest request, CancellationToken cancellationToken);
    Task<VacationRequestResponse> UpdateVacationRequestAsync(int id, UpdateVacationRequestRequest request, Guid userId, CancellationToken cancellationToken);
    Task<VacationRequestResponse> GetVacationRequestAsync(int id, CancellationToken cancellationToken);
    Task<List<VacationRequestResponse>> GetUserVacationRequestsAsync(Guid userId, CancellationToken cancellationToken);
    Task<List<VacationRequestResponse>> GetAllVacationRequestsAsync(CancellationToken cancellationToken, string? department = null);
    Task<VacationRequestResponse> ApproveByManagerAsync(int id, ApproveVacationRequest request, CancellationToken cancellationToken);
    Task<VacationRequestResponse> AuthorizeByHRAsync(int id, AuthorizeVacationRequest request, CancellationToken cancellationToken);
    Task DeleteVacationRequestAsync(int id, Guid userId, bool isManager, CancellationToken cancellationToken);
    Task<List<CalendarDayResponse>> GetCalendarAsync(int month, int year, CancellationToken cancellationToken, string? department = null);
    Task<UserVacationBalanceResponse> CreateUserVacationBalanceAsync(CreateUserVacationBalanceRequest request, CancellationToken cancellationToken);
    Task<UserVacationBalanceResponse> UpdateUserVacationBalanceAsync(int id, UpdateUserVacationBalanceRequest request, CancellationToken cancellationToken);
    Task<UserVacationBalanceResponse> GetUserVacationBalanceAsync(Guid userId, int year, CancellationToken cancellationToken);
    Task<List<UserVacationBalanceResponse>> GetUserVacationBalancesAsync(Guid userId, CancellationToken cancellationToken);
    Task ProcessCompletedVacationsAsync(CancellationToken cancellationToken);
    Task<List<string>> GetDepartmentsAsync(CancellationToken cancellationToken);
    Task<List<UserVacationBalanceResponse>> GetAllUserVacationBalancesAsync(CancellationToken cancellationToken, string? department = null);
}
