using Microsoft.EntityFrameworkCore;
using solvace.vacations.domain.Entities;

namespace solvace.vacations.infra.Contexts;

public class VacationContext : DbContext
{
    public VacationContext(DbContextOptions<VacationContext> options) : base(options)
    {
    }

    public DbSet<VacationRequest> VacationRequests { get; set; }
    public DbSet<UserVacationBalance> UserVacationBalances { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<VacationRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.StartDate).IsRequired();
            entity.Property(e => e.EndDate).IsRequired();
            entity.Property(e => e.BusinessDays).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.ManagerNotes).HasMaxLength(1000);
            entity.Property(e => e.HRNotes).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.StartDate, e.EndDate });
        });

        modelBuilder.Entity<UserVacationBalance>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.AvailableDays).IsRequired();
            entity.Property(e => e.UsedDays).IsRequired();
            entity.Property(e => e.AcquisitionPeriodStart).IsRequired();
            entity.Property(e => e.AcquisitionPeriodEnd).IsRequired();
            entity.Property(e => e.UsagePeriodStart).IsRequired();
            entity.Property(e => e.UsagePeriodEnd).IsRequired();
            entity.Property(e => e.Year).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.AcquisitionPeriodStart, e.AcquisitionPeriodEnd }).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.UsagePeriodStart, e.UsagePeriodEnd });
        });
    }
}
