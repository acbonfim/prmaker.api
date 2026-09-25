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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
            .HasColumnType("longtext");

        modelBuilder.Entity<UserPluginConfiguration>(b =>
        {
            b.ToTable("UserPluginConfigurations");
            b.Property(x => x.Options).HasColumnType("longtext");
            b.HasIndex(x => new { x.PluginId, x.UserExternalId }).IsUnique();
            b.HasIndex(x => x.UserExternalId);
            b.HasOne(x => x.Plugin)
                .WithMany()
                .HasForeignKey(x => x.PluginId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}



