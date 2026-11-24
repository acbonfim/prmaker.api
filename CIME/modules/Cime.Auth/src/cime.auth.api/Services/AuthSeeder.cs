using cliqx.auth.api.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProSales.Repository.Contexts;

namespace Services;

/// <summary>
/// Dados iniciais da autenticação, só com o banco vazio (feature 0014). Substitui o HasData, que
/// usava valores aleatórios (mudavam a cada build) e a senha do admin fixa no código.
/// <list type="bullet">
/// <item>Papéis padrão (ids 1–4, como no seed antigo), se não houver nenhum papel.</item>
/// <item>Papéis garantidos (0099): cada papel de <see cref="EnsuredRoles"/> que faltar é criado (idempotente por
/// nome, sem caixa) — inclusive em produção, onde os dados vieram migrados do SQL Server.</item>
/// <item>Admin inicial, se não houver nenhum usuário e <c>Seed:AdminPassword</c> estiver configurada.</item>
/// </list>
/// </summary>
public static class AuthSeeder
{
    private static readonly (int Id, string Name, string Normalized)[] DefaultRoles =
    {
        (1, "admin", "ADMIN"),
        (2, "user", "USER"),
        (3, "external_client", "EXTERNALCLIENT"),
        (4, "support", "SUPPORT"),
    };

    /// <summary>
    /// Papéis que precisam existir em qualquer banco (0099): <c>gestor</c> (lançamentos/aprovações) e
    /// <c>management</c> (Mesa de gestão — PO / gestor de problemas, sem Bancada).
    /// </summary>
    public static readonly string[] EnsuredRoles = { "admin", "user", "support", "gestor", "management" };

    /// <summary>Papéis de <see cref="EnsuredRoles"/> que faltam entre os existentes (comparação sem caixa).</summary>
    public static IReadOnlyList<string> MissingRoles(IEnumerable<string?> existing)
    {
        var have = new HashSet<string>(existing.Where(n => !string.IsNullOrWhiteSpace(n))!, StringComparer.OrdinalIgnoreCase);
        return EnsuredRoles.Where(r => !have.Contains(r)).ToList();
    }

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        var context = services.GetRequiredService<DefaultContext>();

        if (!await context.Roles.AnyAsync())
        {
            foreach (var (id, name, normalized) in DefaultRoles)
                context.Roles.Add(new Role { Id = id, Name = name, NormalizedName = normalized, ConcurrencyStamp = Guid.NewGuid().ToString() });
            await context.SaveChangesAsync();
            // Ids explícitos não avançam a identity: ajusta a sequência para o próximo papel.
            await context.Database.ExecuteSqlRawAsync(
                $"SELECT setval(pg_get_serial_sequence('{DefaultContext.Schema}.\"AspNetRoles\"', 'Id'), (SELECT MAX(\"Id\") FROM {DefaultContext.Schema}.\"AspNetRoles\"))");
            logger.LogInformation("Seed: papéis padrão criados.");
        }

        await EnsureRolesAsync(context, logger);

        if (await context.Users.AnyAsync())
            return;

        var password = services.GetRequiredService<IConfiguration>()["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed: banco sem usuários e Seed:AdminPassword vazia; admin inicial não criado.");
            return;
        }

        var userManager = services.GetRequiredService<UserManager<User>>();
        var admin = new User
        {
            UserName = "admin",
            Email = "admin@cime.com.br",
            EmailConfirmed = true,
            FullName = "Admin",
            Departamento = "ADMIN",
            CompanyId = 1,
            ChannelOrigin = "WEB",
            Active = true,
            DataUltimoLogin = DateTime.Now,
        };

        var created = await userManager.CreateAsync(admin, password);
        if (!created.Succeeded)
            throw new InvalidOperationException("Seed: falha ao criar o admin inicial: " +
                string.Join("; ", created.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(admin, "admin");
        logger.LogInformation("Seed: admin inicial criado.");
    }

    private static async Task EnsureRolesAsync(DefaultContext context, ILogger logger)
    {
        var existing = await context.Roles.AsNoTracking().Select(r => r.Name).ToListAsync();
        var missing = MissingRoles(existing);
        if (missing.Count == 0)
            return;

        // Banco migrado pode ter a sequência atrás do maior id: alinha antes de inserir sem id explícito.
        await context.Database.ExecuteSqlRawAsync(
            $"SELECT setval(pg_get_serial_sequence('{DefaultContext.Schema}.\"AspNetRoles\"', 'Id'), GREATEST((SELECT COALESCE(MAX(\"Id\"), 0) FROM {DefaultContext.Schema}.\"AspNetRoles\"), 1))");
        foreach (var name in missing)
            context.Roles.Add(new Role { Name = name, NormalizedName = name.ToUpperInvariant(), ConcurrencyStamp = Guid.NewGuid().ToString() });
        await context.SaveChangesAsync();
        logger.LogInformation("Seed: papéis garantidos criados: {Roles}.", string.Join(", ", missing));
    }
}
