using Microsoft.EntityFrameworkCore;
using solvace.prform.domain.Entities;

namespace solvace.prform.Infra.Contexts;

public class DefaultContext(DbContextOptions<DefaultContext> options) : DbContext(options)
{
    public DbSet<Form> Forms { get; set; }
    public DbSet<PullRequestRegister> PullRequests { get; set; }
    public DbSet<PluginConfiguration> PluginConfigurations { get; set; }
    public DbSet<Plugin> Plugins { get; set; }
}



