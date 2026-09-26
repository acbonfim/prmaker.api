using Cime.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using solvace.timeline.domain.Entities;

namespace solvace.timeline.infra.Contexts;

public class TimelineContext : DbContext
{
    public TimelineContext(DbContextOptions<TimelineContext> options) : base(options)
    {
    }

    public DbSet<TimelineEntry> TimelineEntries { get; set; }

    /// <summary>Schema do PostgreSQL deste módulo (feature 0015).</summary>
    public const string Schema = "timeline";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // DateTime como no MySQL (Kind descartado, JSON sem "Z"); ver PostgresConventions (0015).
        configurationBuilder.UseUnspecifiedDateTimes();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<TimelineEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            // text (sem limite): registros longos (análises em markdown) não podem ser cortados (0006).
            entity.Property(e => e.Description).IsRequired().HasColumnType("text");
            entity.Property(e => e.UserName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.UserId);
            entity.Property(e => e.SourceMessageId).HasMaxLength(200);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.CardNumber);

            // Dedup de importações: no índice único múltiplos NULL são permitidos, então registros
            // internos (sem origem) não conflitam; mensagens importadas não duplicam.
            entity.HasIndex(e => e.SourceMessageId).IsUnique();
        });
    }
}
