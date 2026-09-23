using Microsoft.EntityFrameworkCore;
using solvace.prform.Infra.Contexts;
using solvace.timeline.infra.Contexts;
using solvace.vacations.infra.Contexts;

namespace solvace.prform.api.Startup;

/// <summary>
/// Aplica as migrations dos contexts MySQL de forma segura para múltiplas instâncias.
/// Todos os contexts (Default, Vacation, Timeline) compartilham o mesmo servidor MySQL
/// (ConnectionStrings:DefaultConnection), então um único advisory lock do MySQL
/// (GET_LOCK) garante que apenas UMA instância migra por vez. Falha é fatal: se as
/// migrations não aplicarem, o app não sobe e o Cloud Run mantém a revisão anterior.
/// </summary>
public static class StartupMigrator
{
    private const string LockName = "cime_prform_migrations";
    private const int LockTimeoutSeconds = 120;

    public static async Task MigrateMySqlWithLockAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // O lock é mantido nesta conexão do DefaultContext. GET_LOCK é escopado ao
        // servidor MySQL, então protege as migrations de TODOS os contexts.
        var defaultContext = sp.GetRequiredService<DefaultContext>();
        var conn = defaultContext.Database.GetDbConnection();
        await conn.OpenAsync();

        try
        {
            await using (var acquire = conn.CreateCommand())
            {
                acquire.CommandText = "SELECT GET_LOCK(@name, @timeout)";
                AddParam(acquire, "@name", LockName);
                AddParam(acquire, "@timeout", LockTimeoutSeconds);

                var acquired = await acquire.ExecuteScalarAsync();
                if (acquired is null || Convert.ToInt64(acquired) != 1)
                    throw new InvalidOperationException(
                        $"Não foi possível obter o lock de migração '{LockName}' em {LockTimeoutSeconds}s.");
            }

            logger.LogInformation("Lock de migração adquirido. Aplicando migrations MySQL...");

            try
            {
                // A conexão do DefaultContext já está aberta (mantém o lock); os demais
                // contexts usam suas próprias conexões, mas nenhuma outra instância
                // consegue o lock enquanto este bloco roda.
                await MigrateAsync<DefaultContext>(sp, logger);
                await MigrateAsync<VacationContext>(sp, logger);
                await MigrateAsync<TimelineContext>(sp, logger);
            }
            finally
            {
                await using var release = conn.CreateCommand();
                release.CommandText = "SELECT RELEASE_LOCK(@name)";
                AddParam(release, "@name", LockName);
                await release.ExecuteScalarAsync();
                logger.LogInformation("Lock de migração liberado.");
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static async Task MigrateAsync<TContext>(IServiceProvider sp, ILogger logger)
        where TContext : DbContext
    {
        var context = sp.GetRequiredService<TContext>();
        await context.Database.MigrateAsync();
        logger.LogInformation("Migrations de {Context} aplicadas com sucesso.", typeof(TContext).Name);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
