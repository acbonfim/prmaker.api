namespace solvace.vacations.domain.Requests;

public class CreateVacationRequestRequest
{
    public Guid UserId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int BusinessDays { get; set; }
}
