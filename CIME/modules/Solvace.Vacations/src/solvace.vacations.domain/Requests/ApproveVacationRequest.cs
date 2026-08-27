namespace solvace.vacations.domain.Requests;

public class ApproveVacationRequest
{
    public Guid ManagerId { get; set; }
    public string? Notes { get; set; }
}
