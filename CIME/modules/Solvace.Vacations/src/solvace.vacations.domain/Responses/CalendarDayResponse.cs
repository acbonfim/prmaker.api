namespace solvace.vacations.domain.Responses;

public class CalendarDayResponse
{
    public DateTime Date { get; set; }
    public bool IsOccupied { get; set; }
    public List<VacationOccupancy> Occupancies { get; set; } = new();
}

public class VacationOccupancy
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int VacationRequestId { get; set; }
}
