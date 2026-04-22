namespace solvace.vacations.domain.Requests;

public class UpdateVacationRequestRequest
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int BusinessDays { get; set; }
}
