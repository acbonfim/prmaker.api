namespace solvace.vacations.domain.Requests;

public class AuthorizeVacationRequest
{
    public Guid HRId { get; set; }
    public string? Notes { get; set; }
}
