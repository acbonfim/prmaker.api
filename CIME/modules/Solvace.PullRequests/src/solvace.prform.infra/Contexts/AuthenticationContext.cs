using Microsoft.EntityFrameworkCore;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Entities.User;

namespace solvace.prform.Infra.Contexts;

/// <summary>
/// Leitura (somente) dos usuários da autenticação: nome e departamento pelo ExternalId.
/// Banco da Cime.Auth (PostgreSQL, schema "auth", feature 0014). Sem migrações aqui: o schema é da auth.
/// </summary>
public class AuthenticationContext(DbContextOptions<AuthenticationContext> options) : DbContext(options)
{
    public const string Schema = "auth";

    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().ToTable("AspNetUsers", Schema);
    }
}



