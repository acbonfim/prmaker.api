using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProSales.Repository.Contexts;

namespace Services;

/// <summary>
/// Aplica as migrations do banco PostgreSQL de forma segura para múltiplas instâncias.
/// Usa advisory lock de sessão (pg_try_advisory_lock) para garantir que apenas UMA instância
/// migra por vez. Falha é propagada (fatal): se as migrations não aplicarem, o app não sobe e o
/// Cloud Run mantém a revisão anterior servindo.
/// </summary>
public class MigrationService
{
    // Chave fixa do lock (advisory locks do Postgres são por número). Só a auth usa esta chave.
    private const long LockKey = 0x43494D45_41555448; // "CIMEAUTH"
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(2);

    /// <param name="afterMigrate">Roda ainda com o lock (ex.: seed), para duas instâncias não semearem juntas.</param>
    public static async Task ApplyMigrationsAsync(DefaultContext context, ILogger logger, Func<Task>? afterMigrate = null)
    {
        var conn = context.Database.GetDbConnection();
        await conn.OpenAsync();

        try
        {
            await AcquireLockAsync(conn);
            logger.LogInformation("Lock de migração adquirido. Aplicando migrations PostgreSQL...");

            try
            {
                // O EF usa a conexão já aberta (a mesma que segura o lock).
                await context.Database.MigrateAsync();
                logger.LogInformation("Migrations do DefaultContext (auth) aplicadas com sucesso.");
                if (afterMigrate is not null)
                    await afterMigrate();
            }
            finally
            {
                await ExecuteAsync(conn, $"SELECT pg_advisory_unlock({LockKey})");
                logger.LogInformation("Lock de migração liberado.");
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static async Task AcquireLockAsync(DbConnection conn)
    {
        // pg_advisory_lock espera para sempre; tentativa com prazo evita pendurar o startup.
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            if (await ExecuteAsync(conn, $"SELECT pg_try_advisory_lock({LockKey})") is true)
                return;
            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException(
                    $"Não foi possível obter o lock de migração da auth em {LockTimeout.TotalSeconds:0}s.");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task<object?> ExecuteAsync(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }
}
