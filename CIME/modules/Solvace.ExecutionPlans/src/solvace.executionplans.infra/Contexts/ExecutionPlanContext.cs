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
    public DbSet<ExecutionCodeRef> CodeRefs { get; set; }
    public DbSet<ExecutionSuggestion> Suggestions { get; set; }
    // 0080: Especialista do caso.
    public DbSet<CaseChat> CaseChats { get; set; }
    public DbSet<CaseMessage> CaseMessages { get; set; }
    public DbSet<CaseFile> CaseFiles { get; set; }
    public DbSet<CaseFileContent> CaseFileContents { get; set; }
    public DbSet<CasePoint> CasePoints { get; set; }
    public DbSet<CaseProposal> CaseProposals { get; set; }
    public DbSet<CaseAction> CaseActions { get; set; }
    // 0085: itens de trabalho do card.
    public DbSet<WorkItem> WorkItems { get; set; }
    public DbSet<WorkItemTask> WorkItemTasks { get; set; }
    public DbSet<WorkItemLog> WorkItemLogs { get; set; }
    public DbSet<WorkItemFile> WorkItemFiles { get; set; }
    public DbSet<WorkItemFileContent> WorkItemFileContents { get; set; }

    /// <summary>0085: sequência global dos números de item e sub-tarefa (<c>#0000000</c>).</summary>
    public const string WorkItemNumberSequence = "work_item_number_seq";

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
            // 0075: situação do card, contexto da passagem e Dev Test in QA automático.
            entity.Property(e => e.Handoff).HasColumnType("text");
            entity.Property(e => e.HandoffBy).HasMaxLength(200);
            entity.Property(e => e.SituationKind).HasMaxLength(20);
            entity.Property(e => e.DevTestInQaError).HasMaxLength(ExecutionPlan.MaxDevTestInQaErrorLength);
            entity.Property(e => e.EvidenceError).HasMaxLength(ExecutionPlan.MaxEvidenceTextLength);
            entity.Property(e => e.EvidenceSkipReason).HasMaxLength(ExecutionPlan.MaxEvidenceTextLength);
            entity.Property(e => e.SituationPostedText).HasColumnType("text");
            entity.Property(e => e.SituationPostedBy).HasMaxLength(200);
            entity.Ignore(e => e.QaValidationStep);
            // 0080: divergência entre a análise e a base real.
            entity.Property(e => e.DivergenceText).HasMaxLength(ExecutionPlan.MaxDivergenceLength);
            entity.Property(e => e.DivergenceStepKey).HasMaxLength(ExecutionStep.MaxKeyLength);
            entity.Property(e => e.DivergenceConfirmedBy).HasMaxLength(200);
            entity.Ignore(e => e.DivergencePending);
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
            // 0076: evidência do DevOps (não baixa de novo).
            entity.Property(e => e.ExternalKey).HasMaxLength(ExecutionArtifact.MaxExternalKeyLength);
            entity.Ignore(e => e.IsEvidence);
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
            entity.Property(e => e.ChecksStatus).HasMaxLength(20);
            entity.Property(e => e.ChecksFailed).HasMaxLength(ExecutionLink.MaxChecksFailedLength);
            entity.Property(e => e.ChecksHeadSha).HasMaxLength(64);
            entity.Property(e => e.ReviewersPending).HasMaxLength(ExecutionLink.MaxReviewersLength);
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

        // 0075: fotografias do código citado (a lista sai por projeção, sem Snapshot/Proposal/Implemented).
        modelBuilder.Entity<ExecutionCodeRef>(entity =>
        {
            entity.ToTable("ExecutionCodeRefs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Ref).IsRequired().HasMaxLength(ExecutionCodeRef.MaxRefLength);
            entity.Property(e => e.Repository).HasMaxLength(ExecutionCodeRef.MaxRepositoryLength);
            entity.Property(e => e.Path).IsRequired().HasMaxLength(ExecutionCodeRef.MaxPathLength);
            entity.Property(e => e.CommitSha).HasMaxLength(64);
            entity.Property(e => e.Branch).HasMaxLength(ExecutionCodeRef.MaxBranchLength);
            entity.Property(e => e.Language).HasMaxLength(ExecutionCodeRef.MaxLanguageLength);
            entity.Property(e => e.Change).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Title).HasMaxLength(ExecutionCodeRef.MaxTitleLength);
            entity.Property(e => e.Why).HasMaxLength(ExecutionCodeRef.MaxWhyLength);
            entity.Property(e => e.Snapshot).HasColumnType("text");
            entity.Property(e => e.Proposal).HasColumnType("text");
            entity.Property(e => e.Implemented).HasColumnType("text");
            entity.Property(e => e.ImplementedSha).HasMaxLength(64);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.OwnsMany(e => e.Annotations, a => a.ToJson());

            entity.HasOne<ExecutionPlan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.PlanId, e.Ref }).IsUnique();
            entity.HasIndex(e => new { e.CardNumber, e.Ref });
        });

        // 0078: assistente do card (sugestões que não travam o plano).
        modelBuilder.Entity<ExecutionSuggestion>(entity =>
        {
            entity.ToTable("ExecutionSuggestions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(40);
            entity.Property(e => e.DedupKey).IsRequired().HasMaxLength(120);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(ExecutionSuggestion.MaxTitleLength);
            entity.Property(e => e.Text).HasMaxLength(ExecutionSuggestion.MaxTextLength);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ChosenAction).HasMaxLength(40);
            entity.Property(e => e.Result).HasMaxLength(ExecutionSuggestion.MaxResultLength);
            entity.Property(e => e.ResolvedBy).HasMaxLength(200);
            entity.OwnsMany(e => e.Actions, a => a.ToJson());
            entity.Ignore(e => e.IsOpen);

            entity.HasOne<ExecutionPlan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.CardNumber, e.DedupKey }).IsUnique();
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

            // Um pedido ativo por card: dois cliques (ou tela + regra) não abrem duas sessões. 0080: por pista — o
            // especialista do caso tem a dele (um ativo por card) e não disputa com a análise/correção.
            entity.HasIndex(e => e.CardNumber, "IX_ExecutionRequests_ActiveCard")
                .IsUnique()
                .HasFilter("\"Status\" IN ('queued', 'claimed', 'running') AND \"Kind\" NOT IN ('specialist', 'work-item', 'work-item-undo')");
            entity.HasIndex(e => e.CardNumber, "IX_ExecutionRequests_ActiveCaseCard")
                .IsUnique()
                .HasFilter("\"Status\" IN ('queued', 'claimed', 'running') AND \"Kind\" = 'specialist'");
            // 0085: itens de trabalho rodam em paralelo (pista própria por item) — um pedido ativo por item.
            entity.HasIndex(e => e.WorkItemId, "IX_ExecutionRequests_ActiveWorkItem")
                .IsUnique()
                .HasFilter("\"Status\" IN ('queued', 'claimed', 'running') AND \"Kind\" IN ('work-item', 'work-item-undo')");
            entity.HasIndex(e => new { e.CardNumber, e.CreatedAt });
            entity.HasIndex(e => new { e.OwnerUserId, e.Status });
            entity.HasIndex(e => new { e.WorkerId, e.Status });
        });

        // 0080: Especialista do caso — conversa por card, arquivos (conteúdo em tabela própria), pontos, propostas e ações.
        modelBuilder.Entity<CaseChat>(entity =>
        {
            entity.ToTable("CaseChats");
            entity.HasKey(e => e.CardNumber);
            entity.Property(e => e.CardNumber).HasMaxLength(100);
            entity.Property(e => e.SessionId).HasMaxLength(ExecutionSession.MaxSessionIdLength);
            entity.Property(e => e.SessionHost).HasMaxLength(ExecutionSession.MaxHostLength);
            entity.Property(e => e.SessionCwd).HasMaxLength(ExecutionSession.MaxCwdLength);
            entity.Property(e => e.CostUsd).HasPrecision(12, 4);
            entity.Property(e => e.Version).IsRowVersion();
        });

        modelBuilder.Entity<CaseMessage>(entity =>
        {
            entity.ToTable("CaseMessages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.AuthorKind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.AuthorName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Text).IsRequired().HasColumnType("text");
            entity.Property(e => e.Transcript).HasMaxLength(CaseMessage.MaxTranscriptLength);
            entity.Property(e => e.Mentions).HasColumnType("jsonb");
            entity.Property(e => e.Steps).HasColumnType("jsonb");
            entity.Property(e => e.Model).HasMaxLength(ExecutionModelUsage.MaxModelLength);
            entity.Ignore(e => e.IsDeleted);
            entity.Ignore(e => e.FromSpecialist);
            entity.Ignore(e => e.FromPerson);
            entity.Ignore(e => e.MentionsSpecialist);

            entity.HasIndex(e => new { e.CardNumber, e.Number }).IsUnique();
            entity.HasIndex(e => e.RequestId);
        });

        modelBuilder.Entity<CaseFile>(entity =>
        {
            entity.ToTable("CaseFiles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(CaseFile.MaxNameLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(150);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.HasOne(e => e.Content).WithOne().HasForeignKey<CaseFileContent>(c => c.FileId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.CardNumber, e.Number }).IsUnique();
            entity.HasIndex(e => e.MessageId);
        });

        modelBuilder.Entity<CaseFileContent>(entity =>
        {
            entity.ToTable("CaseFileContents");
            entity.HasKey(e => e.FileId);
            entity.Property(e => e.Data).IsRequired().HasColumnType("bytea");
        });

        modelBuilder.Entity<CasePoint>(entity =>
        {
            entity.ToTable("CasePoints");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Text).IsRequired().HasMaxLength(CasePoint.MaxTextLength);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.CardNumber);
        });

        modelBuilder.Entity<CaseProposal>(entity =>
        {
            entity.ToTable("CaseProposals");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(CaseProposal.MaxTitleLength);
            entity.Property(e => e.Summary).HasColumnType("text");
            entity.Property(e => e.Phase).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.DecidedBy).HasMaxLength(200);
            entity.Property(e => e.RejectReason).HasMaxLength(500);
            entity.OwnsMany(e => e.Changes, c => c.ToJson());
            entity.Ignore(e => e.IsPending);
            entity.HasIndex(e => new { e.CardNumber, e.Number }).IsUnique();
        });

        modelBuilder.Entity<CaseAction>(entity =>
        {
            entity.ToTable("CaseActions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(CaseAction.MaxTitleLength);
            entity.Property(e => e.Detail).HasMaxLength(CaseAction.MaxDetailLength);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.DecidedBy).HasMaxLength(200);
            entity.Property(e => e.Result).HasMaxLength(CaseAction.MaxResultLength);
            entity.HasIndex(e => new { e.CardNumber, e.Number }).IsUnique();
        });

        // 0085: itens de trabalho — número da sequência global (item e sub-tarefa), JSON em jsonb.
        modelBuilder.HasSequence<long>(WorkItemNumberSequence, Schema).StartsAt(1).IncrementsBy(1).HasMax(9_999_999);

        modelBuilder.Entity<WorkItem>(entity =>
        {
            entity.ToTable("WorkItems");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Number).HasDefaultValueSql($"nextval('{Schema}.{WorkItemNumberSequence}')").ValueGeneratedOnAdd();
            entity.Property(e => e.CardNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(WorkItem.MaxTitleLength);
            entity.Property(e => e.Description).HasColumnType("text");
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Origin).IsRequired().HasMaxLength(20);
            entity.Property(e => e.TemplateKey).HasMaxLength(WorkItem.MaxTemplateKeyLength);
            entity.Property(e => e.Fingerprint).IsRequired().HasMaxLength(64);
            entity.Property(e => e.CreatedByName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CancelReason).HasMaxLength(WorkItem.MaxReasonLength);
            entity.Property(e => e.LinksJson).HasColumnName("Links").IsRequired().HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
            entity.Property(e => e.Version).IsRowVersion();
            entity.Ignore(e => e.Links);
            entity.Ignore(e => e.Editable);
            entity.Ignore(e => e.OrderedTasks);
            entity.Ignore(e => e.TasksToUndo);

            entity.HasMany(e => e.Tasks).WithOne().HasForeignKey(t => t.WorkItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Logs).WithOne().HasForeignKey(l => l.WorkItemId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.Number).IsUnique();
            entity.HasIndex(e => e.CardNumber);
            entity.HasIndex(e => e.PlanId);
            entity.HasIndex(e => new { e.CardNumber, e.Fingerprint });
        });

        modelBuilder.Entity<WorkItemTask>(entity =>
        {
            entity.ToTable("WorkItemTasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Number).HasDefaultValueSql($"nextval('{Schema}.{WorkItemNumberSequence}')").ValueGeneratedOnAdd();
            entity.Property(e => e.Title).IsRequired().HasMaxLength(WorkItemTask.MaxTitleLength);
            entity.Property(e => e.Description).HasColumnType("text");
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(WorkItemTask.MaxKindLength);
            entity.Property(e => e.Executor).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ParamsJson).HasColumnName("Params").IsRequired().HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.StepKey).HasMaxLength(ExecutionStep.MaxKeyLength);
            entity.Property(e => e.Result).HasColumnType("text");
            entity.Property(e => e.OutputJson).HasColumnName("Output").IsRequired().HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            entity.Property(e => e.UndoJson).HasColumnName("Undo").HasColumnType("jsonb");
            entity.Ignore(e => e.Params);
            entity.Ignore(e => e.OutputElement);
            entity.Ignore(e => e.Undo);

            entity.HasIndex(e => e.Number).IsUnique();
            entity.HasIndex(e => new { e.WorkItemId, e.Order });
        });

        modelBuilder.Entity<WorkItemLog>(entity =>
        {
            entity.ToTable("WorkItemLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityByDefaultColumn();
            entity.Property(e => e.Actor).IsRequired().HasMaxLength(WorkItemLog.MaxActorLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(WorkItemLog.MaxKindLength);
            entity.Property(e => e.Text).IsRequired().HasMaxLength(WorkItemLog.MaxTextLength);

            entity.HasIndex(e => new { e.WorkItemId, e.Id });
        });

        // 0085 r5: arquivos do item (número por item; mesmo tipo + nome substitui; conteúdo em tabela própria).
        modelBuilder.Entity<WorkItemFile>(entity =>
        {
            entity.ToTable("WorkItemFiles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(ExecutionArtifact.MaxNameLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Sha256).IsRequired().HasMaxLength(64);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);

            entity.HasOne<WorkItem>().WithMany().HasForeignKey(e => e.WorkItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Content).WithOne().HasForeignKey<WorkItemFileContent>(c => c.FileId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.WorkItemId, e.Number }).IsUnique();
            entity.HasIndex(e => new { e.WorkItemId, e.Kind, e.Name }).IsUnique();
        });

        modelBuilder.Entity<WorkItemFileContent>(entity =>
        {
            entity.ToTable("WorkItemFileContents");
            entity.HasKey(e => e.FileId);
            entity.Property(e => e.Data).IsRequired().HasColumnType("bytea");
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
