using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace Cime.BuildingBlocks.Persistence;

/// <summary>
/// Advisory lock de sessão do PostgreSQL para as migrations no startup: só UMA instância migra por
/// vez. Espera com prazo (pg_advisory_lock esperaria para sempre e penduraria o startup).
/// </summary>
public static class PostgresMigrationLock
{
    public static async Task RunAsync(DbConnection openConnection, long lockKey, TimeSpan timeout, ILogger logger, Func<Task> work)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (await ScalarAsync(openConnection, $"SELECT pg_try_advisory_lock({lockKey})") is not true)
        {
            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException($"Não foi possível obter o lock de migração {lockKey} em {timeout.TotalSeconds:0}s.");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        logger.LogInformation("Lock de migração adquirido.");
        try
        {
            await work();
        }
        finally
        {
            await ScalarAsync(openConnection, $"SELECT pg_advisory_unlock({lockKey})");
            logger.LogInformation("Lock de migração liberado.");
        }
    }

    private static async Task<object?> ScalarAsync(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }
}
