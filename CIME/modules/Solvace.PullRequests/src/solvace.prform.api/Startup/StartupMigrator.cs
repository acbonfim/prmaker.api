using Cime.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using solvace.prform.Infra.Contexts;
using solvace.timeline.infra.Contexts;
using solvace.vacations.infra.Contexts;

namespace solvace.prform.api.Startup;

/// <summary>
/// Aplica as migrations dos contexts do host de forma segura para múltiplas instâncias.
/// Os contexts (Default, Vacation, Timeline) ficam no mesmo database PostgreSQL, cada um no seu
/// schema (feature 0015); um único advisory lock (chave própria, diferente da auth) garante que
/// apenas UMA instância migra por vez. Falha é fatal: se as migrations não aplicarem, o app não
/// sobe e o Cloud Run mantém a revisão anterior.
/// </summary>
public static class StartupMigrator
{
    private const long LockKey = 0x43494D45_50524652; // "CIMEPRFR"
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(2);

    public static async Task MigratePostgresWithLockAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // O lock é mantido nesta conexão do DefaultContext; o advisory lock vale para o database
        // inteiro, então protege as migrations de TODOS os contexts.
        var defaultContext = sp.GetRequiredService<DefaultContext>();
        var conn = defaultContext.Database.GetDbConnection();
        await conn.OpenAsync();

        try
        {
            await PostgresMigrationLock.RunAsync(conn, LockKey, LockTimeout, logger, async () =>
            {
                // A conexão do DefaultContext já está aberta (mantém o lock); os demais contexts
                // usam suas próprias conexões, mas nenhuma outra instância obtém o lock enquanto isso.
                await MigrateAsync<DefaultContext>(sp, logger);
                await MigrateAsync<VacationContext>(sp, logger);
                await MigrateAsync<TimelineContext>(sp, logger);
            });
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
}
