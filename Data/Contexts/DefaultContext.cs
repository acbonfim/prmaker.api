using Microsoft.EntityFrameworkCore;
using solvace.prform.Data.Entities;

namespace solvace.prform.Data.Contexts;

public class DefaultContext : DbContext
{
    public DefaultContext(DbContextOptions<DefaultContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Form> Forms { get; set; } = null!;
    public DbSet<PullRequestRegister> PullRequests { get; set; } = null!;
}



