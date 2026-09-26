using System.Data.Common;
using System.Globalization;
using System.Text;
using MySqlConnector;
using Npgsql;

namespace Cime.DataMigrator;

public static class Db
{
    public static async Task<List<Column>> ReadColumnsAsync(DbCommand cmd)
    {
        var list = new List<Column>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new Column(r.GetString(0), r.GetString(1), MaxLength(r.IsDBNull(2) ? null : r.GetValue(2)),
                Convert.ToBoolean(r.GetValue(3)), r.FieldCount > 4 && !r.IsDBNull(4) ? r.GetString(4) : null));
        return list;
    }

    /// <summary>Tamanho máximo de texto; os do longtext do MySQL (4 GB) não cabem em int e valem "sem limite".</summary>
    private static int? MaxLength(object? value) =>
        value is null ? null : Convert.ToInt64(value) is var n && n <= int.MaxValue ? (int)n : null;

    public static async Task<List<Column>> TargetColumnsAsync(NpgsqlConnection conn, string schema, string table)
    {
        const string sql = """
            SELECT column_name, data_type, character_maximum_length, is_identity = 'YES'
            FROM information_schema.columns
            WHERE table_schema = @s AND table_name = @t ORDER BY ordinal_position
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("s", schema);
        cmd.Parameters.AddWithValue("t", table);
        return await ReadColumnsAsync(cmd);
    }

    public static string TargetSelect(TableSpec t, IEnumerable<string> cols) =>
        $"SELECT {string.Join(", ", cols.Select(Q))} FROM {t.Target} ORDER BY {string.Join(", ", t.Key.Select(Q))}";

    public static string Q(string ident) => $"\"{ident}\"";

    public static async Task<List<(string, long)>> CountAsync(DbConnection conn, IEnumerable<string> tables, Func<string, string> sql)
    {
        var list = new List<(string, long)>();
        foreach (var t in tables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql(t);
            list.Add((t, Convert.ToInt64(await cmd.ExecuteScalarAsync())));
        }
        return list;
    }

    /// <summary>Datas com precisão de microssegundo (o Postgres guarda 6 casas; o datetime2 tem 7).</summary>
    public static DateTime TruncateToMicroseconds(DateTime d) => new(d.Ticks - d.Ticks % 10, d.Kind);

    /// <summary>Data zerada do MySQL ('0000-00-00'): não existe no Postgres.</summary>
    public static bool IsInvalidDate(object? value) => value is MySqlDateTime { IsValidDateTime: false };

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
            "uuid" => AsGuid(value),
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
            "uuid" => AsGuid(value).ToString("D"),
            "integer" or "bigint" or "smallint" => Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture),
            _ => value is string s ? $"{s.Length}:{s}" : Convert.ToString(value, CultureInfo.InvariantCulture)!,
        };
    }

    private static Guid AsGuid(object v) => v switch
    {
        Guid g => g,
        byte[] b when b.Length == 16 => new Guid(b),
        _ => Guid.Parse(v.ToString()!),
    };

    private static DateTime AsDateTime(object v) => v switch
    {
        DateTime d => d,
        DateTimeOffset o => o.DateTime,
        MySqlDateTime m => m.GetDateTime(),
        _ => Convert.ToDateTime(v, CultureInfo.InvariantCulture),
    };

    /// <summary>Instante em UTC. Na origem MySQL, datetime sem fuso guarda UTC (DateTimeOffset.UtcNow via Pomelo).</summary>
    private static DateTime AsUtc(object v) => v switch
    {
        DateTimeOffset o => o.UtcDateTime,
        DateTime d => d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc),
        MySqlDateTime m => DateTime.SpecifyKind(m.GetDateTime(), DateTimeKind.Utc),
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
