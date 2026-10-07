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
    public DbSet<ExecutionRequest> Requests { get; set; }
    public DbSet<ExecutionWorker> Workers { get; set; }
    public DbSet<ExecutionUserSettings> UserSettings { get; set; }

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
            // 0070: pendências do usuário (GET pending, a cada minuto por aba aberta)
            entity.HasIndex(e => new { e.CreatedByUserId, e.Status });

            // 0033: sessões do Claude Code (retomar e custo) — JSON no próprio plano.
            entity.OwnsMany(e => e.Sessions, s =>
            {
                s.ToJson();
                s.Property(x => x.SessionId).HasMaxLength(ExecutionSession.MaxSessionIdLength);
                s.Property(x => x.Host).HasMaxLength(ExecutionSession.MaxHostLength);
                s.Property(x => x.Cwd).HasMaxLength(ExecutionSession.MaxCwdLength);
                // 0047: consumo por modelo e a linha de base por modelo, dentro do mesmo JSON da sessão.
                s.OwnsMany(x => x.Models, m => m.Property(x => x.Model).HasMaxLength(ExecutionModelUsage.MaxModelLength));
                s.OwnsMany(x => x.BaseModels, m => m.Property(x => x.Model).HasMaxLength(ExecutionModelUsage.MaxModelLength));
                // 0055: de onde a sessão leu e os arquivos de código explorados (com as linhas de base).
                s.OwnsMany(x => x.Sources, r => r.Property(x => x.Key).HasMaxLength(ExecutionReadSource.MaxKeyLength));
                s.OwnsMany(x => x.BaseSources, r => r.Property(x => x.Key).HasMaxLength(ExecutionReadSource.MaxKeyLength));
                s.OwnsMany(x => x.ExploredFiles, f => f.Property(x => x.Path).HasMaxLength(ExecutionExploredFile.MaxPathLength));
                s.OwnsMany(x => x.BaseExploredFiles, f => f.Property(x => x.Path).HasMaxLength(ExecutionExploredFile.MaxPathLength));
            });
            entity.Property(e => e.ResumeRequestedBy).HasMaxLength(200);
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
            entity.Property(e => e.WaitingOn).HasMaxLength(20);
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
            entity.Property(e => e.CancelReason).HasMaxLength(ExecutionQuestion.MaxCancelReasonLength);
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

        // 0039: fila de execução, executores e configurações do usuário.
        modelBuilder.Entity<ExecutionRequest>(entity =>
        {
            entity.ToTable("ExecutionRequests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.SessionId).HasMaxLength(ExecutionSession.MaxSessionIdLength);
            entity.Property(e => e.SessionHost).HasMaxLength(ExecutionSession.MaxHostLength);
            entity.Property(e => e.SessionCwd).HasMaxLength(ExecutionSession.MaxCwdLength);
            entity.Property(e => e.OwnerName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.RequestedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.WorkerName).HasMaxLength(ExecutionWorker.MaxNameLength);
            entity.Property(e => e.Note).HasMaxLength(ExecutionRequest.MaxNoteLength);
            entity.Property(e => e.WaitReason).HasMaxLength(500);
            entity.Property(e => e.LastError).HasMaxLength(ExecutionRequest.MaxErrorLength);
            entity.Property(e => e.StderrTail).HasColumnType("text");
            entity.Property(e => e.FinishedReason).HasMaxLength(ExecutionRequest.MaxErrorLength);
            entity.Property(e => e.FinishedBy).HasMaxLength(200);
            entity.Property(e => e.CostUsd).HasPrecision(12, 4);
            entity.Property(e => e.SessionCostUsd).HasPrecision(12, 4);
            entity.Property(e => e.Model).HasMaxLength(ExecutionRequest.MaxModelLength);
            entity.Property(e => e.Phase).HasMaxLength(ExecutionRequest.MaxPhaseLength);
            entity.Property(e => e.CurrentActivity).HasMaxLength(ExecutionActivity.MaxLabelLength);
            entity.Property(e => e.CurrentActivityTool).HasMaxLength(ExecutionActivity.MaxToolLength);
            entity.Property(e => e.RecentActivities).HasColumnType("text");
            entity.Property(e => e.Version).IsRowVersion();
            entity.Ignore(e => e.IsActive);
            entity.Ignore(e => e.IsFinished);

            // Um pedido ativo por card: dois cliques (ou tela + regra) não abrem duas sessões.
            entity.HasIndex(e => e.CardNumber)
                .IsUnique()
                .HasFilter("\"Status\" IN ('queued', 'claimed', 'running')")
                .HasDatabaseName("IX_ExecutionRequests_ActiveCard");
            entity.HasIndex(e => new { e.CardNumber, e.CreatedAt });
            entity.HasIndex(e => new { e.OwnerUserId, e.Status });
            entity.HasIndex(e => new { e.WorkerId, e.Status });
        });

        modelBuilder.Entity<ExecutionWorker>(entity =>
        {
            entity.ToTable("ExecutionWorkers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.OwnerName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(ExecutionWorker.MaxNameLength);
            entity.Property(e => e.Host).IsRequired().HasMaxLength(ExecutionWorker.MaxNameLength);
            entity.Property(e => e.Os).HasMaxLength(100);
            entity.Property(e => e.AgentVersion).HasMaxLength(50);
            entity.Property(e => e.ClaudeVersion).HasMaxLength(100);
            entity.Property(e => e.SkillsVersion).HasMaxLength(100);
            entity.Property(e => e.Workspace).HasMaxLength(500);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Capabilities).HasColumnType("jsonb");
            entity.Property(e => e.Doctor).HasColumnType("jsonb");
            entity.Property(e => e.RevokedBy).HasMaxLength(200);
            entity.Property(e => e.Version).IsRowVersion();
            entity.Ignore(e => e.IsRevoked);
            entity.Ignore(e => e.DoctorPending);

            entity.HasIndex(e => new { e.OwnerUserId, e.Host });
        });

        modelBuilder.Entity<ExecutionUserSettings>(entity =>
        {
            entity.ToTable("ExecutionUserSettings");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.DailyBudgetUsd).HasPrecision(12, 4);
            entity.Property(e => e.AutoAssignedTo).HasMaxLength(200);
            entity.Property(e => e.AutoLastError).HasMaxLength(1000);
        });
    }
}
