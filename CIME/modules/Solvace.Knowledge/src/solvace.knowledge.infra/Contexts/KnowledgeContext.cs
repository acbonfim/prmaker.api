using Cime.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using solvace.knowledge.domain.Entities;

namespace solvace.knowledge.infra.Contexts;

public class KnowledgeContext(DbContextOptions<KnowledgeContext> options) : DbContext(options)
{
    /// <summary>Schema do PostgreSQL deste módulo (feature 0033).</summary>
    public const string Schema = "knowledge";

    public DbSet<KnowledgeArticle> Articles { get; set; }
    public DbSet<KnowledgeSyncState> SyncStates { get; set; }
    public DbSet<ArchitectureProject> Projects { get; set; }
    public DbSet<ArchitectureSection> Sections { get; set; }
    public DbSet<ArchitectureSectionVersion> SectionVersions { get; set; }
    public DbSet<ArchitectureSuggestion> Suggestions { get; set; }
    public DbSet<ArchitectureQuestion> Questions { get; set; }
    public DbSet<ReverseModule> ReverseModules { get; set; }
    public DbSet<ReverseRevision> ReverseRevisions { get; set; }
    public DbSet<ReverseAsset> ReverseAssets { get; set; }
    public DbSet<ReverseIndexEntry> ReverseIndexEntries { get; set; }
    public DbSet<ReverseCardContext> ReverseCardContexts { get; set; }
    public DbSet<ReverseTrap> ReverseTraps { get; set; }
    public DbSet<ReverseInfraSnapshot> ReverseInfraSnapshots { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.UseUnspecifiedDateTimes();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<KnowledgeArticle>(entity =>
        {
            entity.ToTable("KnowledgeArticles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Environment).IsRequired().HasMaxLength(10);
            entity.Property(e => e.SourceId).IsRequired().HasMaxLength(KnowledgeArticle.MaxSourceIdLength);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(KnowledgeArticle.MaxTitleLength);
            entity.Property(e => e.Content).IsRequired().HasColumnType("text");
            entity.Property(e => e.Category).HasMaxLength(300);
            entity.Property(e => e.Subcategory).HasMaxLength(300);
            entity.Property(e => e.Tags).HasColumnType("jsonb");
            entity.Property(e => e.ContentHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.SyncedBy).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => new { e.Environment, e.SourceId }).IsUnique();
            entity.HasIndex(e => new { e.Environment, e.ArticleNumber });
        });

        modelBuilder.Entity<KnowledgeSyncState>(entity =>
        {
            entity.ToTable("KnowledgeSyncStates");
            entity.HasKey(e => e.Environment);
            entity.Property(e => e.Environment).HasMaxLength(10);
            entity.Property(e => e.LastSyncBy).HasMaxLength(200);
            entity.Property(e => e.FilterHash).HasMaxLength(64);
        });

        modelBuilder.Entity<ArchitectureProject>(entity =>
        {
            entity.ToTable("ArchitectureProjects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).IsRequired().HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(ArchitectureProject.MaxNameLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(30);
            entity.Property(e => e.Repository).HasMaxLength(500);
            entity.Property(e => e.Summary).HasMaxLength(ArchitectureProject.MaxSummaryLength);
            entity.Property(e => e.Keywords).HasColumnType("jsonb");
            entity.Property(e => e.SourceCommit).HasMaxLength(64);
            entity.Property(e => e.SourceBranch).HasMaxLength(200);
            // 0038: nome, frase e área para pessoas (não vão para o espelho das skills).
            entity.Property(e => e.DisplayName).HasMaxLength(ArchitectureProject.MaxDisplayNameLength);
            entity.Property(e => e.Tagline).HasMaxLength(ArchitectureProject.MaxTaglineLength);
            entity.Property(e => e.BusinessArea).HasMaxLength(ArchitectureProject.MaxBusinessAreaLength);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.UpdatedBy).IsRequired().HasMaxLength(200);
            entity.HasMany(e => e.Sections).WithOne().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
            // 0034: interdependências como JSON no projeto.
            entity.OwnsMany(e => e.Relations, r => r.ToJson());
            entity.HasIndex(e => e.Key).IsUnique();
        });

        modelBuilder.Entity<ArchitectureSection>(entity =>
        {
            entity.ToTable("ArchitectureSections");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).IsRequired().HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(ArchitectureSection.MaxTitleLength);
            entity.Property(e => e.Content).IsRequired().HasColumnType("text");
            entity.Ignore(e => e.Length);
            entity.Property(e => e.ContentHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(10);
            // 0038: llm (vai para as skills) | human (Guia, só na tela); as seções existentes são técnicas.
            entity.Property(e => e.Audience).IsRequired().HasMaxLength(10).HasDefaultValue(ArchitectureSectionAudience.Llm);
            entity.Property(e => e.UpdatedBy).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => new { e.ProjectId, e.Key }).IsUnique();
        });

        modelBuilder.Entity<ArchitectureSectionVersion>(entity =>
        {
            entity.ToTable("ArchitectureSectionVersions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Title).IsRequired().HasMaxLength(ArchitectureSection.MaxTitleLength);
            entity.Property(e => e.Content).IsRequired().HasColumnType("text");
            entity.Property(e => e.Source).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Note).HasMaxLength(ArchitectureSection.MaxNoteLength);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.HasOne<ArchitectureSection>().WithMany().HasForeignKey(e => e.SectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.SectionId, e.Version }).IsUnique();
        });

        modelBuilder.Entity<ArchitectureSuggestion>(entity =>
        {
            entity.ToTable("ArchitectureSuggestions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ProjectKey).IsRequired().HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.SectionKey).HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Content).IsRequired().HasColumnType("text");
            entity.Property(e => e.CardNumber).HasMaxLength(100);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ResolvedBy).HasMaxLength(200);
            entity.Property(e => e.ResolutionNote).HasMaxLength(500);
            entity.Property(e => e.ItemId).HasMaxLength(20);
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
        });

        // 0040: perguntas do "Pergunte" — as sem resposta viram a fila do admin/skill.
        modelBuilder.Entity<ArchitectureQuestion>(entity =>
        {
            entity.ToTable("ArchitectureQuestions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Text).IsRequired().HasMaxLength(ArchitectureQuestion.MaxTextLength);
            entity.Property(e => e.Normalized).IsRequired().HasMaxLength(ArchitectureQuestion.MaxTextLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Coverage).IsRequired().HasMaxLength(20);
            entity.Property(e => e.SuggestedProject).HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.SuggestedSection).HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.FirstAskedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.LastAskedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.AnsweredProject).HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.AnsweredSection).HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.ResolvedBy).HasMaxLength(200);
            entity.Property(e => e.Note).HasMaxLength(ArchitectureQuestion.MaxNoteLength);
            entity.Ignore(e => e.IsGap);
            entity.HasIndex(e => e.Normalized).IsUnique();
            entity.HasIndex(e => new { e.Status, e.LastAskedAt });
        });

        // 0052: engenharia reversa por módulo — fontes, revisões com aprovação, anexos de UI/UX, índice por item.
        modelBuilder.Entity<ReverseModule>(entity =>
        {
            entity.ToTable("ReverseModules");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).IsRequired().HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.OwnsMany(e => e.Sources, s => s.ToJson());
            entity.Property(e => e.Aliases).HasColumnType("jsonb");
            entity.Property(e => e.SuggestedTerms).HasColumnType("jsonb");
            entity.Property(e => e.DismissedTerms).HasColumnType("jsonb");
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.UpdatedBy).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.Key).IsUnique();
        });

        modelBuilder.Entity<ReverseRevision>(entity =>
        {
            entity.ToTable("ReverseRevisions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ModuleKey).IsRequired().HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.DocType).IsRequired().HasMaxLength(40);
            entity.Property(e => e.Mode).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Content).IsRequired().HasColumnType("text");
            entity.Property(e => e.Summary).HasMaxLength(ReverseRevision.MaxSummaryLength);
            entity.Property(e => e.Lint).HasColumnType("jsonb");
            entity.Property(e => e.Coverage).HasColumnType("jsonb");
            entity.Property(e => e.Session).HasColumnType("jsonb");
            entity.Property(e => e.Progress).HasColumnType("jsonb");
            entity.Property(e => e.SuggestionDecisions).HasColumnType("jsonb");
            entity.Property(e => e.ReviewNote).HasMaxLength(ReverseRevision.MaxNoteLength);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.UpdatedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.SubmittedBy).HasMaxLength(200);
            entity.Property(e => e.ReviewedBy).HasMaxLength(200);
            entity.Property(e => e.PublishedBy).HasMaxLength(200);
            entity.Ignore(e => e.IsOpen);
            entity.HasIndex(e => new { e.ModuleKey, e.DocType, e.Number }).IsUnique();
            entity.HasIndex(e => new { e.Status, e.UpdatedAt });
        });

        modelBuilder.Entity<ReverseAsset>(entity =>
        {
            entity.ToTable("ReverseAssets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ModuleKey).IsRequired().HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Url).HasMaxLength(2000);
            entity.Property(e => e.FileName).HasMaxLength(300);
            entity.Property(e => e.ContentType).HasMaxLength(200);
            entity.Property(e => e.Data).HasColumnType("bytea");
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.Screens).HasColumnType("jsonb");
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => new { e.ModuleKey, e.IsDeleted });
        });

        modelBuilder.Entity<ReverseIndexEntry>(entity =>
        {
            entity.ToTable("ReverseIndexEntries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ModuleKey).IsRequired().HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.DocType).IsRequired().HasMaxLength(40);
            entity.Property(e => e.ItemId).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(300);
            entity.Property(e => e.Body).IsRequired().HasColumnType("text");
            entity.Property(e => e.Tags).HasColumnType("jsonb");
            entity.Property(e => e.Tables).HasColumnType("jsonb");
            entity.Property(e => e.Refs).HasColumnType("jsonb");
            entity.Property(e => e.Evidence).HasColumnType("jsonb");
            entity.Property(e => e.Modules).HasColumnType("jsonb");
            entity.Property(e => e.Synonyms).HasColumnType("jsonb");
            entity.Ignore(e => e.Ref);
            entity.HasIndex(e => new { e.ModuleKey, e.DocType });
            entity.HasIndex(e => new { e.ModuleKey, e.ItemId });
        });

        // 0059: mapa de infra (AWS) por módulo — um por módulo, substituído a cada leitura
        modelBuilder.Entity<ReverseInfraSnapshot>(entity =>
        {
            entity.ToTable("ReverseInfraSnapshots");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ModuleKey).IsRequired().HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.Account).IsRequired().HasMaxLength(40);
            entity.Property(e => e.Data).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.CollectedBy).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.ModuleKey).IsUnique();
        });

        // 0054: armadilhas ligadas aos itens da engenharia reversa
        modelBuilder.Entity<ReverseTrap>(entity =>
        {
            entity.ToTable("ReverseTraps");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ModuleKey).IsRequired().HasMaxLength(ArchitectureProject.MaxKeyLength);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Text).IsRequired().HasColumnType("text");
            entity.Property(e => e.Items).HasColumnType("jsonb");
            entity.Property(e => e.Cards).HasColumnType("jsonb");
            entity.Property(e => e.Origin).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ReviewedBy).HasMaxLength(200);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.UpdatedBy).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => new { e.ModuleKey, e.IsDeleted });
        });

        modelBuilder.Entity<ReverseCardContext>(entity =>
        {
            entity.ToTable("ReverseCardContexts");
            entity.HasKey(e => e.CardNumber);
            entity.Property(e => e.CardNumber).HasMaxLength(100);
            entity.Property(e => e.Modules).HasColumnType("jsonb");
            entity.Property(e => e.ConsultedRefs).HasColumnType("jsonb");
        });
    }
}
