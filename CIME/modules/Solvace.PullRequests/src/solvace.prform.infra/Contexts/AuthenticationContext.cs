using Microsoft.EntityFrameworkCore;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Entities.User;
using solvace.prform.domain.Entities.Usage;

namespace solvace.prform.Infra.Contexts;

/// <summary>
/// Leitura (somente) dos usuários da autenticação: nome e departamento pelo ExternalId, e o histórico de logins (0079).
/// Banco da Cime.Auth (PostgreSQL, schema "auth", feature 0014). Sem migrações aqui: o schema é da auth.
/// </summary>
public class AuthenticationContext(DbContextOptions<AuthenticationContext> options) : DbContext(options)
{
    public const string Schema = "auth";

    public DbSet<User> Users { get; set; }
    /// <summary>Tentativas de login gravadas pela Cime.Auth (0079).</summary>
    public DbSet<LoginEventRecord> LoginEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().ToTable("AspNetUsers", Schema);
        modelBuilder.Entity<LoginEventRecord>(b =>
        {
            b.ToTable("LoginEvents", Schema);
            b.HasKey(x => x.Id);
        });
    }
}



