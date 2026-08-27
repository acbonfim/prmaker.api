namespace solvace.vacations.domain.Requests;

public class UpdateUserVacationBalanceRequest
{
    public int AvailableDays { get; set; }
    public DateTime AcquisitionPeriodStart { get; set; }
    public DateTime AcquisitionPeriodEnd { get; set; }
}
