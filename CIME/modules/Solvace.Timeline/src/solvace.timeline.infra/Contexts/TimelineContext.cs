using Microsoft.EntityFrameworkCore;
using solvace.timeline.domain.Entities;

namespace solvace.timeline.infra.Contexts;

public class TimelineContext : DbContext
{
    public TimelineContext(DbContextOptions<TimelineContext> options) : base(options)
    {
    }

    public DbSet<TimelineEntry> TimelineEntries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TimelineEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.UserName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.UserId);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.CardNumber);
        });
    }
}
