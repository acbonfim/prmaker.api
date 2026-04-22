namespace solvace.vacations.domain.Requests;

public class CreateUserVacationBalanceRequest
{
    public Guid UserId { get; set; }
    public int AvailableDays { get; set; }
    public DateTime AcquisitionPeriodStart { get; set; }
    public DateTime AcquisitionPeriodEnd { get; set; }
}
