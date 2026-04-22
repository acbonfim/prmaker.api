using solvace.vacations.application.Contracts;
using solvace.vacations.domain.Entities;
using solvace.vacations.domain.Entities.Base;
using solvace.vacations.domain.Enums;
using solvace.vacations.domain.Requests;
using solvace.vacations.domain.Responses;

namespace solvace.vacations.application;

public class VacationApplication : IVacationApplication
{
    private readonly IVacationRepository _vacationRepository;
    private readonly IUserVacationBalanceRepository _balanceRepository;
    private readonly IUserRepository _userRepository;

    public VacationApplication(
        IVacationRepository vacationRepository,
        IUserVacationBalanceRepository balanceRepository,
        IUserRepository userRepository)
    {
        _vacationRepository = vacationRepository;
        _balanceRepository = balanceRepository;
        _userRepository = userRepository;
    }

    public async Task<VacationRequestResponse> CreateVacationRequestAsync(
        CreateVacationRequestRequest request,
        CancellationToken cancellationToken)
    {
        var balance = await _balanceRepository.GetActiveBalanceForDateAsync(request.UserId, request.StartDate, cancellationToken);

        // Criar saldo automaticamente se não existir (padrão: 30 dias)
        if (balance == null)
        {
            var acquisitionStart = new DateTime(request.StartDate.Year, 1, 1);
            var acquisitionEnd = new DateTime(request.StartDate.Year, 12, 31);
            balance = new UserVacationBalance(request.UserId, 30, acquisitionStart, acquisitionEnd);
            balance = await _balanceRepository.CreateAsync(balance, cancellationToken);
        }

        if (!balance.IsValidForDate(request.StartDate))
            throw new DomainException("Vacation dates are outside the usage period for this balance");

        if (balance.GetRemainingDays() < request.BusinessDays)
            throw new DomainException($"Insufficient vacation days. Available: {balance.GetRemainingDays()}, Requested: {request.BusinessDays}");

        var hasConflict = await _vacationRepository.HasConflictingDatesAsync(
            request.UserId, request.StartDate, request.EndDate, null, cancellationToken);

        if (hasConflict)
            throw new DomainException("You already have a vacation request for these dates");

        var vacation = new VacationRequest(request.UserId, request.StartDate, request.EndDate, request.BusinessDays);
        var created = await _vacationRepository.CreateAsync(vacation, cancellationToken);

        var fullName = await _userRepository.GetFullNameAsync(request.UserId, cancellationToken);
        return MapToResponse(created, fullName);
    }

    public async Task<VacationRequestResponse> UpdateVacationRequestAsync(
        int id,
        UpdateVacationRequestRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var vacation = await _vacationRepository.GetByIdAsync(id, cancellationToken);

        if (vacation == null)
            throw new DomainException("Vacation request not found");

        if (vacation.UserId != userId)
            throw new DomainException("You can only update your own vacation requests");

        if (!vacation.CanBeEditedByUser())
            throw new DomainException("Cannot edit vacation request after manager approval");

        var balance = await _balanceRepository.GetActiveBalanceForDateAsync(userId, request.StartDate, cancellationToken);

        if (balance == null)
            throw new DomainException("User does not have vacation balance for this period");

        if (!balance.IsValidForDate(request.StartDate))
            throw new DomainException("Vacation dates are outside the usage period for this balance");

        if (balance.GetRemainingDays() < request.BusinessDays)
            throw new DomainException($"Insufficient vacation days. Available: {balance.GetRemainingDays()}, Requested: {request.BusinessDays}");

        var hasConflict = await _vacationRepository.HasConflictingDatesAsync(
            userId, request.StartDate, request.EndDate, id, cancellationToken);

        if (hasConflict)
            throw new DomainException("You already have a vacation request for these dates");

        vacation.UpdateDates(request.StartDate, request.EndDate, request.BusinessDays);
        var updated = await _vacationRepository.UpdateAsync(vacation, cancellationToken);

        var fullName = await _userRepository.GetFullNameAsync(userId, cancellationToken);
        return MapToResponse(updated, fullName);
    }

    public async Task<VacationRequestResponse> GetVacationRequestAsync(int id, CancellationToken cancellationToken)
    {
        var vacation = await _vacationRepository.GetByIdAsync(id, cancellationToken);

        if (vacation == null)
            throw new DomainException("Vacation request not found");

        var fullName = await _userRepository.GetFullNameAsync(vacation.UserId, cancellationToken);
        return MapToResponse(vacation, fullName);
    }

    public async Task<List<VacationRequestResponse>> GetUserVacationRequestsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var vacations = await _vacationRepository.GetByUserIdAsync(userId, cancellationToken);
        var fullName = await _userRepository.GetFullNameAsync(userId, cancellationToken);
        return vacations.Select(v => MapToResponse(v, fullName)).ToList();
    }

    public async Task<List<VacationRequestResponse>> GetAllVacationRequestsAsync(CancellationToken cancellationToken)
    {
        var vacations = await _vacationRepository.GetAllAsync(cancellationToken);
        var userIds = vacations.Select(v => v.UserId).Distinct();
        var names = await _userRepository.GetFullNamesAsync(userIds, cancellationToken);
        return vacations.Select(v => MapToResponse(v, names.TryGetValue(v.UserId, out var n) ? n : null)).ToList();
    }

    public async Task<VacationRequestResponse> ApproveByManagerAsync(
        int id,
        ApproveVacationRequest request,
        CancellationToken cancellationToken)
    {
        var vacation = await _vacationRepository.GetByIdAsync(id, cancellationToken);

        if (vacation == null)
            throw new DomainException("Vacation request not found");

        vacation.ApproveByManager(request.ManagerId, request.Notes);
        var updated = await _vacationRepository.UpdateAsync(vacation, cancellationToken);

        var fullName = await _userRepository.GetFullNameAsync(vacation.UserId, cancellationToken);
        return MapToResponse(updated, fullName);
    }

    public async Task<VacationRequestResponse> AuthorizeByHRAsync(
        int id,
        AuthorizeVacationRequest request,
        CancellationToken cancellationToken)
    {
        var vacation = await _vacationRepository.GetByIdAsync(id, cancellationToken);

        if (vacation == null)
            throw new DomainException("Vacation request not found");

        var balance = await _balanceRepository.GetActiveBalanceForDateAsync(
            vacation.UserId, vacation.StartDate, cancellationToken);

        if (balance == null)
            throw new DomainException("User vacation balance not found");

        vacation.AuthorizeByHR(request.HRId, request.Notes);
        balance.AddUsedDays(vacation.BusinessDays);

        await _vacationRepository.UpdateAsync(vacation, cancellationToken);
        await _balanceRepository.UpdateAsync(balance, cancellationToken);

        var fullName = await _userRepository.GetFullNameAsync(vacation.UserId, cancellationToken);
        return MapToResponse(vacation, fullName);
    }

    public async Task DeleteVacationRequestAsync(
        int id,
        Guid userId,
        bool isManager,
        CancellationToken cancellationToken)
    {
        var vacation = await _vacationRepository.GetByIdAsync(id, cancellationToken);

        if (vacation == null)
            throw new DomainException("Vacation request not found");

        if (isManager)
        {
            if (!vacation.CanBeDeletedByManager())
                throw new DomainException("Cannot delete this vacation request");
        }
        else
        {
            if (vacation.UserId != userId)
                throw new DomainException("You can only delete your own vacation requests");

            if (!vacation.CanBeDeletedByUser())
                throw new DomainException("Cannot delete vacation request after manager approval");
        }

        if (vacation.Status == VacationStatus.AuthorizedByHR || vacation.Status == VacationStatus.Completed)
        {
            var balance = await _balanceRepository.GetActiveBalanceForDateAsync(
                vacation.UserId, vacation.StartDate, cancellationToken);

            if (balance != null)
            {
                balance.RemoveUsedDays(vacation.BusinessDays);
                await _balanceRepository.UpdateAsync(balance, cancellationToken);
            }
        }

        await _vacationRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task<List<CalendarDayResponse>> GetCalendarAsync(
        int month,
        int year,
        CancellationToken cancellationToken)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var vacations = await _vacationRepository.GetByDateRangeAsync(startDate, endDate, cancellationToken);

        var calendar = new Dictionary<DateTime, CalendarDayResponse>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            calendar[date.Date] = new CalendarDayResponse
            {
                Date = date.Date,
                IsOccupied = false,
                Occupancies = new List<VacationOccupancy>()
            };
        }

        var approvedVacations = vacations
            .Where(v => v.Status == VacationStatus.AuthorizedByHR || v.Status == VacationStatus.Completed)
            .ToList();

        var userIds = approvedVacations.Select(v => v.UserId).Distinct();
        var names = await _userRepository.GetFullNamesAsync(userIds, cancellationToken);

        foreach (var vacation in approvedVacations)
        {
            for (var date = vacation.StartDate; date <= vacation.EndDate; date = date.AddDays(1))
            {
                if (calendar.ContainsKey(date.Date))
                {
                    calendar[date.Date].IsOccupied = true;
                    calendar[date.Date].Occupancies.Add(new VacationOccupancy
                    {
                        UserId = vacation.UserId,
                        UserName = names.TryGetValue(vacation.UserId, out var n) ? n : vacation.UserId.ToString(),
                        VacationRequestId = vacation.Id
                    });
                }
            }
        }

        return calendar.Values.OrderBy(c => c.Date).ToList();
    }

    public async Task<UserVacationBalanceResponse> CreateUserVacationBalanceAsync(
        CreateUserVacationBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _balanceRepository.GetByUserIdAndAcquisitionPeriodAsync(
            request.UserId, request.AcquisitionPeriodStart, request.AcquisitionPeriodEnd, cancellationToken);

        if (existing != null)
            throw new DomainException("User already has vacation balance for this acquisition period");

        var balance = new UserVacationBalance(
            request.UserId,
            request.AvailableDays,
            request.AcquisitionPeriodStart,
            request.AcquisitionPeriodEnd);
        var created = await _balanceRepository.CreateAsync(balance, cancellationToken);

        var fullName = await _userRepository.GetFullNameAsync(request.UserId, cancellationToken);
        return MapToBalanceResponse(created, fullName);
    }

    public async Task<UserVacationBalanceResponse> UpdateUserVacationBalanceAsync(
        int id,
        UpdateUserVacationBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var balance = await _balanceRepository.GetByIdAsync(id, cancellationToken);

        if (balance == null)
            throw new DomainException("Vacation balance not found");

        balance.UpdateAcquisitionPeriod(
            request.AcquisitionPeriodStart,
            request.AcquisitionPeriodEnd,
            request.AvailableDays);

        var updated = await _balanceRepository.UpdateAsync(balance, cancellationToken);

        var fullName = await _userRepository.GetFullNameAsync(balance.UserId, cancellationToken);
        return MapToBalanceResponse(updated, fullName);
    }

    public async Task<UserVacationBalanceResponse> GetUserVacationBalanceAsync(
        Guid userId,
        int year,
        CancellationToken cancellationToken)
    {
        var balance = await _balanceRepository.GetByUserIdAndYearAsync(userId, year, cancellationToken);

        if (balance == null)
            throw new DomainException("User vacation balance not found");

        var fullName = await _userRepository.GetFullNameAsync(userId, cancellationToken);
        return MapToBalanceResponse(balance, fullName);
    }

    public async Task<List<UserVacationBalanceResponse>> GetUserVacationBalancesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var balances = await _balanceRepository.GetByUserIdAsync(userId, cancellationToken);
        var fullName = await _userRepository.GetFullNameAsync(userId, cancellationToken);
        return balances.Select(b => MapToBalanceResponse(b, fullName)).ToList();
    }

    public async Task ProcessCompletedVacationsAsync(CancellationToken cancellationToken)
    {
        var vacations = await _vacationRepository.GetAllAsync(cancellationToken);

        foreach (var vacation in vacations.Where(v => v.Status == VacationStatus.AuthorizedByHR && v.StartDate <= DateTime.Today))
        {
            vacation.MarkAsCompleted();
            await _vacationRepository.UpdateAsync(vacation, cancellationToken);
        }
    }

    private VacationRequestResponse MapToResponse(VacationRequest vacation, string? fullName = null)
    {
        return new VacationRequestResponse
        {
            Id = vacation.Id,
            UserId = vacation.UserId,
            UserFullName = fullName,
            StartDate = vacation.StartDate,
            EndDate = vacation.EndDate,
            BusinessDays = vacation.BusinessDays,
            Status = vacation.Status,
            StatusDescription = vacation.Status.ToString(),
            ManagerNotes = vacation.ManagerNotes,
            HRNotes = vacation.HRNotes,
            ApprovedByManagerId = vacation.ApprovedByManagerId,
            ApprovedByManagerAt = vacation.ApprovedByManagerAt,
            AuthorizedByHRId = vacation.AuthorizedByHRId,
            AuthorizedByHRAt = vacation.AuthorizedByHRAt,
            CreatedAt = vacation.CreatedAt,
            UpdatedAt = vacation.UpdatedAt
        };
    }

    private UserVacationBalanceResponse MapToBalanceResponse(UserVacationBalance balance, string? fullName = null)
    {
        return new UserVacationBalanceResponse
        {
            Id = balance.Id,
            UserId = balance.UserId,
            UserFullName = fullName,
            AvailableDays = balance.AvailableDays,
            UsedDays = balance.UsedDays,
            RemainingDays = balance.GetRemainingDays(),
            AcquisitionPeriodStart = balance.AcquisitionPeriodStart,
            AcquisitionPeriodEnd = balance.AcquisitionPeriodEnd,
            UsagePeriodStart = balance.UsagePeriodStart,
            UsagePeriodEnd = balance.UsagePeriodEnd,
            Year = balance.Year,
            IsActive = balance.IsActive(),
            CreatedAt = balance.CreatedAt,
            UpdatedAt = balance.UpdatedAt
        };
    }
}
