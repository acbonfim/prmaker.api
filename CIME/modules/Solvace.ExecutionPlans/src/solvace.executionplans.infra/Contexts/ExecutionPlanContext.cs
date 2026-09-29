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
    public DbSet<ExecutionQuestion> Questions { get; set; }
    public DbSet<ExecutionLink> Links { get; set; }
    public DbSet<ExecutionNote> Notes { get; set; }

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

            entity.Property(e => e.Phase).IsRequired().HasMaxLength(20).HasDefaultValue(ExecutionPhase.Analysis);
            entity.HasOne<ExecutionPlan>().WithMany().HasForeignKey(e => e.ParentPlanId).OnDelete(DeleteBehavior.Restrict);

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
            entity.Property(e => e.Executor).IsRequired().HasMaxLength(20).HasDefaultValue(ExecutionExecutor.Claude);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20).HasDefaultValue(ExecutionStepKind.Task);
            entity.Property(e => e.Repository).HasMaxLength(200);
            entity.Property(e => e.DependsOn).HasColumnType("jsonb");

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
            entity.HasIndex(e => e.NoteId);
        });

        modelBuilder.Entity<ExecutionNote>(entity =>
        {
            entity.ToTable("ExecutionNotes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.StepKey).HasMaxLength(ExecutionStep.MaxKeyLength);
            entity.Property(e => e.Text).IsRequired().HasColumnType("text");
            entity.Property(e => e.AuthorName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.DeletedBy).HasMaxLength(200);
            entity.Ignore(e => e.IsDeleted);

            entity.HasOne<ExecutionPlan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.CardNumber, e.Number });
            entity.HasIndex(e => e.PlanId);
        });

        modelBuilder.Entity<ExecutionQuestion>(entity =>
        {
            entity.ToTable("ExecutionQuestions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.StepKey).HasMaxLength(ExecutionStep.MaxKeyLength);
            entity.Property(e => e.Text).IsRequired().HasColumnType("text");
            // Opções guardadas como JSON (lista pequena, sempre lida junto com a pergunta).
            entity.OwnsMany(e => e.Options, o => o.ToJson());
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Answer).HasColumnType("text");
            entity.Property(e => e.AnsweredBy).HasMaxLength(200);
            entity.Property(e => e.AnsweredVia).HasMaxLength(20);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);

            entity.HasOne<ExecutionPlan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.PlanId, e.Status });
        });

        modelBuilder.Entity<ExecutionLink>(entity =>
        {
            entity.ToTable("ExecutionLinks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.StepKey).IsRequired().HasMaxLength(ExecutionStep.MaxKeyLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(ExecutionLink.MaxUrlLength);
            entity.Property(e => e.Title).HasMaxLength(ExecutionLink.MaxTitleLength);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Repository).HasMaxLength(200);
            entity.Property(e => e.TargetBranch).HasMaxLength(200);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.StatusChangedBy).HasMaxLength(200);
            entity.Ignore(e => e.DisplayName);

            entity.HasOne<ExecutionPlan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.PlanId, e.StepKey });
        });

        modelBuilder.Entity<ExecutionArtifactContent>(entity =>
        {
            entity.ToTable("ExecutionArtifactContents");
            entity.HasKey(e => e.ArtifactId);
            entity.Property(e => e.Data).IsRequired().HasColumnType("bytea");
        });
    }
}
