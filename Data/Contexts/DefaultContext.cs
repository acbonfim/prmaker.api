using Microsoft.EntityFrameworkCore;
using solvace.prform.Data.Entities;

namespace solvace.prform.Data.Contexts;

public class DefaultContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<Form> Forms { get; set; }
    public DbSet<PullRequestRegister> PullRequests { get; set; }
}



