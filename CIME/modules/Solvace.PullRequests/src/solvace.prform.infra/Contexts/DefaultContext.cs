using Cime.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using solvace.prform.domain.Entities;

namespace solvace.prform.Infra.Contexts;

public class DefaultContext(DbContextOptions<DefaultContext> options) : DbContext(options)
{
    public DbSet<Form> Forms { get; set; }
    public DbSet<PullRequestRegister> PullRequests { get; set; }
    public DbSet<PluginConfiguration> PluginConfigurations { get; set; }
    public DbSet<Plugin> Plugins { get; set; }
    public DbSet<HandoverRegister> Handovers { get; set; }
    public DbSet<PullRequestGithub> PullRequestsGithub { get; set; }
    public DbSet<UserPluginConfiguration> UserPluginConfigurations { get; set; }
    /// <summary>Consumo de IA por chamada (0042).</summary>
    public DbSet<AiUsageRecord> AiUsageRecords { get; set; }
    /// <summary>Lotes de promoção de PRs para release/hotfix (0074).</summary>
    public DbSet<PromotionBatch> PromotionBatches { get; set; }
    public DbSet<PromotionItem> PromotionItems { get; set; }

    /// <summary>Schema do PostgreSQL deste módulo (feature 0015).</summary>
    public const string Schema = "prform";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // DateTime como no MySQL (Kind descartado, JSON sem "Z"); ver PostgresConventions (0015).
        configurationBuilder.UseUnspecifiedDateTimes();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // 0042: consumo de IA por chamada (custo por ação, por usuário).
        modelBuilder.Entity<AiUsageRecord>(b =>
        {
            b.ToTable("AiUsageRecords");
            b.HasKey(x => x.Id);
            b.Property(x => x.UserName).IsRequired().HasMaxLength(AiUsageRecord.MaxUserNameLength);
            b.Property(x => x.Action).IsRequired().HasMaxLength(AiUsageRecord.MaxActionLength);
            b.Property(x => x.Route).IsRequired().HasMaxLength(AiUsageRecord.MaxRouteLength);
            b.Property(x => x.Provider).IsRequired().HasMaxLength(AiUsageRecord.MaxProviderLength);
            b.Property(x => x.Model).HasMaxLength(AiUsageRecord.MaxModelLength);
            b.Property(x => x.CostUsd).HasPrecision(14, 6);
            b.Property(x => x.Error).HasMaxLength(AiUsageRecord.MaxErrorLength);
            b.HasIndex(x => new { x.UserExternalId, x.CreatedAt });
            b.HasIndex(x => x.CreatedAt);
        });

        // 0074: promoção de PRs em lote (release/hotfix) — um lote tem N itens (card × repositório × destino).
        modelBuilder.Entity<PromotionBatch>(b =>
        {
            b.ToTable("PromotionBatches");
            b.HasKey(x => x.Id);
            b.Property(x => x.Version).IsRequired().HasMaxLength(PromotionBatch.MaxVersionLength);
            b.Property(x => x.Kind).HasMaxLength(PromotionBatch.MaxKindLength);
            b.Property(x => x.OwnerName).IsRequired().HasMaxLength(PromotionBatch.MaxOwnerNameLength);
            b.HasIndex(x => new { x.OwnerUserId, x.CreatedAt });
            b.HasMany(x => x.Items).WithOne(x => x.Batch).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Items).HasField("_items");
        });

        modelBuilder.Entity<PromotionItem>(b =>
        {
            b.ToTable("PromotionItems");
            b.HasKey(x => x.Id);
            b.Property(x => x.CardNumber).IsRequired().HasMaxLength(PromotionItem.MaxCardLength);
            b.Property(x => x.CardTitle).IsRequired().HasMaxLength(PromotionItem.MaxTitleLength);
            b.Property(x => x.RepositoryId).IsRequired().HasMaxLength(PromotionItem.MaxRepositoryLength);
            b.Property(x => x.SourceBranch).IsRequired().HasMaxLength(PromotionItem.MaxBranchLength);
            b.Property(x => x.SourceBase).HasMaxLength(PromotionItem.MaxBranchLength);
            b.Property(x => x.TargetTemplate).IsRequired().HasMaxLength(PromotionItem.MaxBranchLength);
            b.Property(x => x.TargetBranch).IsRequired().HasMaxLength(PromotionItem.MaxBranchLength);
            b.Property(x => x.Mode).IsRequired().HasMaxLength(20);
            b.Property(x => x.Status).IsRequired().HasMaxLength(30);
            b.Property(x => x.Message).HasMaxLength(PromotionItem.MaxMessageLength);
            b.Property(x => x.NewBranch).HasMaxLength(PromotionItem.MaxBranchLength);
            b.Property(x => x.ConflictFiles).HasMaxLength(PromotionItem.MaxFilesLength);
            b.Property(x => x.PullRequestUrl).HasMaxLength(PromotionItem.MaxUrlLength);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => new { x.BatchId, x.Position });
            b.HasIndex(x => new { x.Status, x.WorkerId });
            b.HasIndex(x => new { x.CardNumber, x.RepositoryId, x.TargetBranch });
        });

        // Handover nasce público. O default no banco garante que os registros já existentes
        // permaneçam públicos após a criação da coluna, até serem alterados manualmente.
        modelBuilder.Entity<HandoverRegister>()
            .Property(x => x.IsPublic)
            .HasDefaultValue(true);

        // Um registro por card (root cause/descrição únicos para todos os repositórios).
        modelBuilder.Entity<PullRequestRegister>(b =>
        {
            b.Property(x => x.CardNumber).HasMaxLength(PullRequestRegister.MaxCardNumberLength);
            b.HasIndex(x => x.CardNumber).IsUnique();
            b.HasMany(x => x.GithubPullRequests)
                .WithOne(x => x.PullRequestRegister)
                .HasForeignKey(x => x.PullRequestRegisterId)
                .OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.GithubPullRequests).HasField("_githubPullRequests");
        });

        modelBuilder.Entity<PullRequestGithub>(b =>
        {
            b.ToTable("PullRequestsGithub");
            b.Property(x => x.CardNumber).HasMaxLength(PullRequestRegister.MaxCardNumberLength);
            b.Property(x => x.RepositoryId).HasMaxLength(PullRequestGithub.MaxRepositoryIdLength);
            b.Property(x => x.BranchPrefix).HasMaxLength(PullRequestGithub.MaxBranchPrefixLength);
            b.Property(x => x.BranchName).HasMaxLength(PullRequestGithub.MaxBranchNameLength);
            b.Property(x => x.TargetBranch).HasMaxLength(PullRequestGithub.MaxBranchNameLength);
            b.Property(x => x.Url).HasMaxLength(PullRequestGithub.MaxUrlLength);
            b.Property(x => x.Title).HasMaxLength(PullRequestGithub.MaxTitleLength);
            b.Property(x => x.Status).HasMaxLength(PullRequestGithub.MaxStatusLength);
            b.HasIndex(x => x.CardNumber);
            b.HasIndex(x => new { x.RepositoryId, x.GithubPrNumber }).IsUnique();
        });

        // Plugins de uso pessoal: um registro por usuário x plugin. O plugin é soft delete,
        // então não há cascade físico — os dados do usuário ficam (e só deixam de ser lidos).
        modelBuilder.Entity<Plugin>()
            .Property(x => x.IsPersonal)
            .HasDefaultValue(false);

        modelBuilder.Entity<Plugin>()
            .Property(x => x.IsOptional)
            .HasDefaultValue(false);

        modelBuilder.Entity<Plugin>()
            .Property(x => x.PersonalFields)
            .HasColumnType("text");

        modelBuilder.Entity<UserPluginConfiguration>(b =>
        {
            b.ToTable("UserPluginConfigurations");
            b.Property(x => x.Options).HasColumnType("text");
            b.HasIndex(x => new { x.PluginId, x.UserExternalId }).IsUnique();
            b.HasIndex(x => x.UserExternalId);
            b.HasOne(x => x.Plugin)
                .WithMany()
                .HasForeignKey(x => x.PluginId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}



