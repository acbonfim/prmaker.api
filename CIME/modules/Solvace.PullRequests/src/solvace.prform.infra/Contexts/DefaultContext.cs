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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Handover nasce público. O default no banco garante que os registros já existentes
        // permaneçam públicos após a criação da coluna, até serem alterados manualmente.
        modelBuilder.Entity<HandoverRegister>()
            .Property(x => x.IsPublic)
            .HasDefaultValue(true);
    }
}



