using solvace.vacations.domain.Entities.Base;

namespace solvace.vacations.domain.Entities;

public class UserVacationBalance : IEntity<int>, IAuditableEntity
{
    public int Id { get; set; }
    public Guid UserId { get; private set; }
    public int AvailableDays { get; private set; }
    public int UsedDays { get; private set; }

    // Período Aquisitivo
    public DateTime AcquisitionPeriodStart { get; private set; }
    public DateTime AcquisitionPeriodEnd { get; private set; }

    // Período de Gozo (quando pode usar as férias)
    public DateTime UsagePeriodStart { get; private set; }
    public DateTime UsagePeriodEnd { get; private set; }

    // Mantido para compatibilidade (calculado a partir do período)
    public int Year { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    protected UserVacationBalance() { }

    public UserVacationBalance(
        Guid userId,
        int availableDays,
        DateTime acquisitionPeriodStart,
        DateTime acquisitionPeriodEnd)
    {
        if (availableDays < 0)
            throw new DomainException("Available days cannot be negative");

        if (acquisitionPeriodStart >= acquisitionPeriodEnd)
            throw new DomainException("Acquisition period start must be before end");

        var periodDays = (acquisitionPeriodEnd - acquisitionPeriodStart).TotalDays;
        if (periodDays < 365 || periodDays > 366)
            throw new DomainException("Acquisition period must be approximately 12 months");

        UserId = userId;
        AvailableDays = availableDays;
        UsedDays = 0;
        AcquisitionPeriodStart = acquisitionPeriodStart.Date;
        AcquisitionPeriodEnd = acquisitionPeriodEnd.Date;

        // Período de gozo: 12 meses após o período aquisitivo
        UsagePeriodStart = acquisitionPeriodEnd.Date.AddDays(1);
        UsagePeriodEnd = UsagePeriodStart.AddYears(1).AddDays(-1);

        // Year baseado no início do período aquisitivo
        Year = acquisitionPeriodStart.Year;

        CreatedAt = DateTimeOffset.UtcNow;
    }

    // Construtor para manter compatibilidade (cria período aquisitivo de 1 ano)
    public UserVacationBalance(Guid userId, int availableDays, int year)
        : this(userId, availableDays, new DateTime(year, 1, 1), new DateTime(year, 12, 31))
    {
    }

    public void UpdateAvailableDays(int days)
    {
        if (days < 0)
            throw new DomainException("Available days cannot be negative");

        AvailableDays = days;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateAcquisitionPeriod(DateTime periodStart, DateTime periodEnd, int availableDays)
    {
        if (periodStart >= periodEnd)
            throw new DomainException("Acquisition period start must be before end");

        var periodDays = (periodEnd - periodStart).TotalDays;
        if (periodDays < 365 || periodDays > 366)
            throw new DomainException("Acquisition period must be approximately 12 months");

        if (availableDays < 0)
            throw new DomainException("Available days cannot be negative");

        AcquisitionPeriodStart = periodStart.Date;
        AcquisitionPeriodEnd = periodEnd.Date;

        // Recalcular período de gozo
        UsagePeriodStart = periodEnd.Date.AddDays(1);
        UsagePeriodEnd = UsagePeriodStart.AddYears(1).AddDays(-1);

        // Atualizar year baseado no novo período
        Year = periodStart.Year;

        AvailableDays = availableDays;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddUsedDays(int days)
    {
        if (days < 0)
            throw new DomainException("Days cannot be negative");

        if (UsedDays + days > AvailableDays)
            throw new DomainException("Cannot use more days than available");

        UsedDays += days;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RemoveUsedDays(int days)
    {
        if (days < 0)
            throw new DomainException("Days cannot be negative");

        if (UsedDays - days < 0)
            throw new DomainException("Cannot have negative used days");

        UsedDays -= days;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public int GetRemainingDays() => AvailableDays - UsedDays;

    public bool IsValidForDate(DateTime date)
    {
        // Verifica se a data está dentro do período de gozo
        return date.Date >= UsagePeriodStart.Date && date.Date <= UsagePeriodEnd.Date;
    }

    public bool IsActive()
    {
        // Período ativo se hoje está dentro ou antes do período de gozo
        var today = DateTime.Today;
        return today <= UsagePeriodEnd;
    }
}
