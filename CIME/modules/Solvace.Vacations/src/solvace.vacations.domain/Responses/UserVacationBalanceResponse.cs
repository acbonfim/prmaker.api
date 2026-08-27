namespace solvace.vacations.domain.Responses;

public class UserVacationBalanceResponse
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string? UserFullName { get; set; }
    public int AvailableDays { get; set; }
    public int UsedDays { get; set; }
    public int RemainingDays { get; set; }

    // Período Aquisitivo
    public DateTime AcquisitionPeriodStart { get; set; }
    public DateTime AcquisitionPeriodEnd { get; set; }

    // Período de Gozo
    public DateTime UsagePeriodStart { get; set; }
    public DateTime UsagePeriodEnd { get; set; }

    // Mantido para compatibilidade
    public int Year { get; set; }

    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
