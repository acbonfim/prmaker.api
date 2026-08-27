using solvace.vacations.domain.Enums;

namespace solvace.vacations.domain.Responses;

public class VacationRequestResponse
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string? UserFullName { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int BusinessDays { get; set; }
    public VacationStatus Status { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
    public string? ManagerNotes { get; set; }
    public string? HRNotes { get; set; }
    public Guid? ApprovedByManagerId { get; set; }
    public DateTime? ApprovedByManagerAt { get; set; }
    public Guid? AuthorizedByHRId { get; set; }
    public DateTime? AuthorizedByHRAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
