using Microsoft.EntityFrameworkCore;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Entities.User;

namespace solvace.prform.Infra.Contexts;

public class AuthenticationContext(DbContextOptions<AuthenticationContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
}



