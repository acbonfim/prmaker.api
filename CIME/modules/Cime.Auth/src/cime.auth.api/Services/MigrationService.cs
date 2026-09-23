using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProSales.Repository.Contexts;

namespace Services;

/// <summary>
/// Aplica as migrations do banco SQL Server de forma segura para múltiplas instâncias.
/// Usa sp_getapplock (lock de aplicação com escopo de sessão) para garantir que apenas
/// UMA instância migra por vez. Falha é propagada (fatal): se as migrations não aplicarem,
/// o app não sobe e o Cloud Run mantém a revisão anterior servindo.
/// </summary>
public class MigrationService
{
    private const string LockResource = "cime_auth_migrations";
    private const int LockTimeoutMs = 120000;

    public static async Task ApplyMigrationsAsync(DefaultContext context, ILogger logger)
    {
        var conn = context.Database.GetDbConnection();
        await conn.OpenAsync();

        try
        {
            var result = await AcquireLockAsync(conn);
            // sp_getapplock: >= 0 sucesso (0 concedido, 1 concedido após espera); < 0 erro/timeout.
            if (result < 0)
                throw new InvalidOperationException(
                    $"Não foi possível obter o lock de migração '{LockResource}' (código {result}).");

            logger.LogInformation("Lock de migração adquirido. Aplicando migrations SQL Server...");

            try
            {
                await context.Database.MigrateAsync();
                logger.LogInformation("Migrations do DefaultContext (auth) aplicadas com sucesso.");
            }
            finally
            {
                await ReleaseLockAsync(conn);
                logger.LogInformation("Lock de migração liberado.");
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static async Task<int> AcquireLockAsync(DbConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "sp_getapplock";
        cmd.CommandType = CommandType.StoredProcedure;

        var ret = cmd.CreateParameter();
        ret.ParameterName = "@RETURN_VALUE";
        ret.Direction = ParameterDirection.ReturnValue;
        cmd.Parameters.Add(ret);

        AddParam(cmd, "@Resource", LockResource);
        AddParam(cmd, "@LockMode", "Exclusive");
        AddParam(cmd, "@LockOwner", "Session");
        AddParam(cmd, "@LockTimeout", LockTimeoutMs);

        await cmd.ExecuteNonQueryAsync();
        return ret.Value is int v ? v : Convert.ToInt32(ret.Value);
    }

    private static async Task ReleaseLockAsync(DbConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "sp_releaseapplock";
        cmd.CommandType = CommandType.StoredProcedure;
        AddParam(cmd, "@Resource", LockResource);
        AddParam(cmd, "@LockOwner", "Session");
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
