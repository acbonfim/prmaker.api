using solvace.vacations.domain.Entities.Base;
using solvace.vacations.domain.Enums;

namespace solvace.vacations.domain.Entities;

public class VacationRequest : IEntity<int>, IAuditableEntity
{
    public int Id { get; set; }
    public Guid UserId { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public int BusinessDays { get; private set; }
    public VacationStatus Status { get; private set; }
    public string? ManagerNotes { get; private set; }
    public string? HRNotes { get; private set; }
    public Guid? ApprovedByManagerId { get; private set; }
    public DateTime? ApprovedByManagerAt { get; private set; }
    public Guid? AuthorizedByHRId { get; private set; }
    public DateTime? AuthorizedByHRAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    protected VacationRequest() { }

    public VacationRequest(Guid userId, DateTime startDate, DateTime endDate, int businessDays)
    {
        if (startDate >= endDate)
            throw new DomainException("Start date must be before end date");

        if (businessDays <= 0)
            throw new DomainException("Business days must be greater than zero");

        if (startDate < DateTime.Today)
            throw new DomainException("Start date cannot be in the past");

        UserId = userId;
        StartDate = startDate;
        EndDate = endDate;
        BusinessDays = businessDays;
        Status = VacationStatus.PendingApproval;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateDates(DateTime startDate, DateTime endDate, int businessDays)
    {
        if (Status != VacationStatus.PendingApproval)
            throw new DomainException("Cannot update dates after manager approval");

        if (startDate >= endDate)
            throw new DomainException("Start date must be before end date");

        if (businessDays <= 0)
            throw new DomainException("Business days must be greater than zero");

        if (startDate < DateTime.Today)
            throw new DomainException("Start date cannot be in the past");

        StartDate = startDate;
        EndDate = endDate;
        BusinessDays = businessDays;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApproveByManager(Guid managerId, string? notes = null)
    {
        if (Status != VacationStatus.PendingApproval)
            throw new DomainException("Can only approve requests with pending status");

        Status = VacationStatus.ApprovedByManager;
        ApprovedByManagerId = managerId;
        ApprovedByManagerAt = DateTime.UtcNow;
        ManagerNotes = notes;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AuthorizeByHR(Guid hrId, string? notes = null)
    {
        if (Status != VacationStatus.ApprovedByManager)
            throw new DomainException("Can only authorize requests approved by manager");

        Status = VacationStatus.AuthorizedByHR;
        AuthorizedByHRId = hrId;
        AuthorizedByHRAt = DateTime.UtcNow;
        HRNotes = notes;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsCompleted()
    {
        if (Status != VacationStatus.AuthorizedByHR)
            throw new DomainException("Can only complete authorized requests");

        if (DateTime.Today < StartDate)
            throw new DomainException("Cannot complete vacation before start date");

        Status = VacationStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(Guid? cancelledBy = null)
    {
        if (Status == VacationStatus.Completed)
            throw new DomainException("Cannot cancel completed vacation");

        Status = VacationStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool CanBeEditedByUser() => Status == VacationStatus.PendingApproval;

    public bool CanBeDeletedByUser() => Status == VacationStatus.PendingApproval;

    public bool CanBeDeletedByManager() => Status != VacationStatus.Completed && Status != VacationStatus.Cancelled;
}
