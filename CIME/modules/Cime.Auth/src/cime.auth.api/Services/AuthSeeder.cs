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
/// <item>Admin inicial, se não houver nenhum usuário e <c>Seed:AdminPassword</c> estiver configurada.</item>
/// </list>
/// Em produção os dados vêm migrados do SQL Server, então nada aqui roda.
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
}
