using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Cime.DataMigrator;

public record Column(string Name, string DataType, int? MaxLength, bool IsIdentity, string? Collation = null);

/// <summary>Banco de origem (somente leitura).</summary>
public interface ISource : IAsyncDisposable
{
    DbConnection Connection { get; }
    string Describe();
    Task<List<Column>> ColumnsAsync(string table);
    string Select(TableSpec t, IEnumerable<string> cols);
    /// <summary>Avisos sobre a origem que mudam o comportamento no Postgres (ex.: collation PAD SPACE).</summary>
    Task<List<string>> NotesAsync();
}

/// <summary>SQL Server, tabelas em dbo (perfil auth).</summary>
public sealed class SqlServerSource : ISource
{
    private readonly SqlConnection _conn;
    public SqlServerSource(string cs) { _conn = new SqlConnection(cs); _conn.Open(); }
    public DbConnection Connection => _conn;
    public string Describe() => $"SQL Server {_conn.ServerVersion} / {_conn.Database}";

    public async Task<List<Column>> ColumnsAsync(string table)
    {
        const string sql = """
            SELECT c.name, t.name, CASE WHEN c.max_length = -1 THEN NULL
                                        WHEN t.name IN ('nvarchar','nchar') THEN c.max_length / 2
                                        ELSE c.max_length END, c.is_identity, c.collation_name
            FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@t) ORDER BY c.column_id
            """;
        await using var cmd = new SqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("@t", $"dbo.[{table}]");
        return await Db.ReadColumnsAsync(cmd);
    }

    public string Select(TableSpec t, IEnumerable<string> cols) =>
        $"SELECT {string.Join(", ", cols.Select(c => $"[{c}]"))} FROM dbo.[{t.Name}] ORDER BY {string.Join(", ", t.Key.Select(k => $"[{k}]"))}";

    public Task<List<string>> NotesAsync() => Task.FromResult(new List<string>());
    public ValueTask DisposeAsync() => _conn.DisposeAsync();
}

/// <summary>MySQL, tabelas no database da connection string (perfil prform).</summary>
public sealed class MySqlSource : ISource
{
    private readonly MySqlConnection _conn;

    public MySqlSource(string cs)
    {
        // Datas zeradas ('0000-00-00') voltam como MySqlDateTime inválido: o check acusa em vez de converter
        // em silêncio. char(36) já volta como Guid e tinyint(1) como bool (padrões do MySqlConnector).
        var b = new MySqlConnectionStringBuilder(cs) { AllowZeroDateTime = true, ConvertZeroDateTime = false };
        _conn = new MySqlConnection(b.ConnectionString);
        _conn.Open();
    }

    public DbConnection Connection => _conn;
    public string Describe() => $"MySQL {_conn.ServerVersion} / {_conn.Database}";

    public async Task<List<Column>> ColumnsAsync(string table)
    {
        const string sql = """
            SELECT column_name, data_type, character_maximum_length, extra LIKE '%auto_increment%', collation_name
            FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = @t ORDER BY ordinal_position
            """;
        await using var cmd = new MySqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("@t", table);
        return await Db.ReadColumnsAsync(cmd);
    }

    public string Select(TableSpec t, IEnumerable<string> cols) =>
        $"SELECT {string.Join(", ", cols.Select(c => $"`{c}`"))} FROM `{t.Name}` ORDER BY {string.Join(", ", t.Key.Select(k => $"`{k}`"))}";

    public async Task<List<string>> NotesAsync()
    {
        const string sql = """
            SELECT DISTINCT c.collation_name, co.pad_attribute
            FROM information_schema.columns c JOIN information_schema.collations co ON co.collation_name = c.collation_name
            WHERE c.table_schema = DATABASE() AND c.collation_name IS NOT NULL
            """;
        var notes = new List<string>();
        await using var cmd = new MySqlCommand(sql, _conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var pad = r.GetString(1);
            notes.Add($"collation de texto na origem: {r.GetString(0)} ({pad})" +
                      (pad == "PAD SPACE" ? " — ignora espaço no fim na comparação; o Postgres não ignora" : ""));
        }
        return notes;
    }

    public ValueTask DisposeAsync() => _conn.DisposeAsync();
}
