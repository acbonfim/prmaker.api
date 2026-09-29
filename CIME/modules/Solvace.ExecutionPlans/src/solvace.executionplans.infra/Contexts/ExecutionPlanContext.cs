using Cime.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.infra.Contexts;

public class ExecutionPlanContext : DbContext
{
    public ExecutionPlanContext(DbContextOptions<ExecutionPlanContext> options) : base(options)
    {
    }

    public DbSet<ExecutionPlan> Plans { get; set; }
    public DbSet<ExecutionStep> Steps { get; set; }
    public DbSet<ExecutionLog> Logs { get; set; }
    public DbSet<ExecutionArtifact> Artifacts { get; set; }
    public DbSet<ExecutionArtifactContent> ArtifactContents { get; set; }

    /// <summary>Schema do PostgreSQL deste módulo (feature 0023).</summary>
    public const string Schema = "execution";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.UseUnspecifiedDateTimes();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ExecutionPlan>(entity =>
        {
            entity.ToTable("ExecutionPlans");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(ExecutionPlan.MaxTitleLength);
            entity.Property(e => e.Summary).HasColumnType("text");
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.StatusReason).HasMaxLength(ExecutionStep.MaxReasonLength);
            entity.Property(e => e.StatusChangedBy).HasMaxLength(200);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            // xmin: toda alteração do plano (inclusive via etapas/logs, que tocam UpdatedAt) é serializada.
            entity.Property(e => e.Version).IsRowVersion();

            entity.HasMany(e => e.Steps).WithOne().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.CardNumber, e.CreatedAt });
        });

        modelBuilder.Entity<ExecutionStep>(entity =>
        {
            entity.ToTable("ExecutionSteps");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).IsRequired().HasMaxLength(ExecutionStep.MaxKeyLength);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(ExecutionStep.MaxTitleLength);
            entity.Property(e => e.Description).HasColumnType("text");
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.StatusReason).HasMaxLength(ExecutionStep.MaxReasonLength);
            entity.Property(e => e.StatusChangedBy).HasMaxLength(200);
            entity.Property(e => e.Activity).HasMaxLength(ExecutionStep.MaxActivityLength);
            entity.Property(e => e.Checkpoint).HasColumnType("text");

            entity.HasIndex(e => new { e.PlanId, e.Key }).IsUnique();
        });

        modelBuilder.Entity<ExecutionLog>(entity =>
        {
            entity.ToTable("ExecutionLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StepKey).HasMaxLength(ExecutionStep.MaxKeyLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Message).IsRequired().HasColumnType("text");
            entity.Property(e => e.ClientId).HasMaxLength(ExecutionLog.MaxClientIdLength);

            entity.HasOne<ExecutionPlan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.PlanId, e.Id });
            // Dedup do reenvio da fila local (NULL não conflita).
            entity.HasIndex(e => new { e.PlanId, e.ClientId }).IsUnique();
        });

        modelBuilder.Entity<ExecutionArtifact>(entity =>
        {
            entity.ToTable("ExecutionArtifacts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.StepKey).HasMaxLength(ExecutionStep.MaxKeyLength);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(ExecutionArtifact.MaxNameLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Sha256).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Description).HasMaxLength(ExecutionArtifact.MaxDescriptionLength);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);

            entity.HasOne<ExecutionPlan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Content).WithOne().HasForeignKey<ExecutionArtifactContent>(c => c.ArtifactId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.PlanId, e.Kind, e.Name }).IsUnique();
        });

        modelBuilder.Entity<ExecutionArtifactContent>(entity =>
        {
            entity.ToTable("ExecutionArtifactContents");
            entity.HasKey(e => e.ArtifactId);
            entity.Property(e => e.Data).IsRequired().HasColumnType("bytea");
        });
    }
}
