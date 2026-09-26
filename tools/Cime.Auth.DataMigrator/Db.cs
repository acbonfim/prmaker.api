using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Cime.Auth.DataMigrator;

public record Column(string Name, string DataType, int? MaxLength, bool IsIdentity);

public static class Db
{
    public static async Task<List<Column>> SourceColumnsAsync(SqlConnection conn, string table)
    {
        const string sql = """
            SELECT c.name, t.name, CASE WHEN c.max_length = -1 THEN NULL
                                        WHEN t.name IN ('nvarchar','nchar') THEN c.max_length / 2
                                        ELSE c.max_length END, c.is_identity
            FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@t) ORDER BY c.column_id
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@t", $"dbo.[{table}]");
        return await ReadColumnsAsync(cmd);
    }

    public static async Task<List<Column>> TargetColumnsAsync(NpgsqlConnection conn, string table)
    {
        const string sql = """
            SELECT column_name, data_type, character_maximum_length, is_identity = 'YES'
            FROM information_schema.columns
            WHERE table_schema = @s AND table_name = @t ORDER BY ordinal_position
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("s", Tables.TargetSchema);
        cmd.Parameters.AddWithValue("t", table);
        return await ReadColumnsAsync(cmd);
    }

    private static async Task<List<Column>> ReadColumnsAsync(DbCommand cmd)
    {
        var list = new List<Column>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new Column(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2)), Convert.ToBoolean(r.GetValue(3))));
        return list;
    }

    public static string SourceSelect(TableSpec t, IEnumerable<string> cols) =>
        $"SELECT {string.Join(", ", cols.Select(c => $"[{c}]"))} FROM dbo.[{t.Name}] ORDER BY {string.Join(", ", t.Key.Select(k => $"[{k}]"))}";

    public static string TargetSelect(TableSpec t, IEnumerable<string> cols) =>
        $"SELECT {string.Join(", ", cols.Select(Q))} FROM {Tables.TargetSchema}.{Q(t.Name)} ORDER BY {string.Join(", ", t.Key.Select(Q))}";

    public static string Q(string ident) => $"\"{ident}\"";

    /// <summary>Datas com precisão de microssegundo (o Postgres guarda 6 casas; o datetime2 tem 7).</summary>
    public static DateTime TruncateToMicroseconds(DateTime d) => new(d.Ticks - d.Ticks % 10, d.Kind);

    /// <summary>Valor convertido para gravar no Postgres, conforme o tipo da coluna de destino.</summary>
    public static object ToTarget(object? value, Column target)
    {
        if (value is null or DBNull) return DBNull.Value;
        return target.DataType switch
        {
            "timestamp without time zone" => DateTime.SpecifyKind(TruncateToMicroseconds(AsDateTime(value)), DateTimeKind.Unspecified),
            "timestamp with time zone" => TruncateToMicroseconds(AsUtc(value)),
            "boolean" => Convert.ToBoolean(value),
            "integer" => Convert.ToInt32(value),
            "bigint" => Convert.ToInt64(value),
            "uuid" => value is Guid g ? g : Guid.Parse(value.ToString()!),
            _ => value,
        };
    }

    /// <summary>
    /// Representação canônica de um valor para o hash de verificação: a mesma para origem e destino
    /// quando o dado é o mesmo (guiada pelo tipo da coluna de destino).
    /// </summary>
    public static string Canonical(object? value, Column target)
    {
        if (value is null or DBNull) return "∅";
        return target.DataType switch
        {
            "timestamp without time zone" => TruncateToMicroseconds(AsDateTime(value)).ToString("yyyy-MM-ddTHH:mm:ss.ffffff", CultureInfo.InvariantCulture),
            "timestamp with time zone" => TruncateToMicroseconds(AsUtc(value)).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture),
            "boolean" => Convert.ToBoolean(value) ? "1" : "0",
            "uuid" => (value is Guid g ? g : Guid.Parse(value.ToString()!)).ToString("D"),
            "integer" or "bigint" or "smallint" => Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture),
            _ => value is string s ? $"{s.Length}:{s}" : Convert.ToString(value, CultureInfo.InvariantCulture)!,
        };
    }

    public static string RowKey(IReadOnlyDictionary<string, object?> row, TableSpec t, IReadOnlyDictionary<string, Column> target) =>
        string.Join("|", t.Key.Select(k => Canonical(row[k], target[k])));

    private static DateTime AsDateTime(object v) => v switch
    {
        DateTime d => d,
        DateTimeOffset o => o.DateTime,
        _ => Convert.ToDateTime(v, CultureInfo.InvariantCulture),
    };

    private static DateTime AsUtc(object v) => v switch
    {
        DateTimeOffset o => o.UtcDateTime,
        DateTime d => d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc),
        _ => throw new InvalidOperationException($"Valor {v.GetType().Name} inesperado para timestamptz"),
    };

    public static int CodePoints(string s)
    {
        var n = 0;
        foreach (var _ in s.EnumerateRunes()) n++;
        return n;
    }

    public static string Describe(Exception e)
    {
        var sb = new StringBuilder(e.Message);
        for (var inner = e.InnerException; inner is not null; inner = inner.InnerException)
            sb.Append(" → ").Append(inner.Message);
        return sb.ToString();
    }
}
