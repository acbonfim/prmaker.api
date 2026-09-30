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
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.UpdatedBy).IsRequired().HasMaxLength(200);
            entity.HasMany(e => e.Sections).WithOne().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(e => e.ContentHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(10);
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
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
        });
    }
}
