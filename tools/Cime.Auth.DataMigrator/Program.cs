using Cime.Auth.DataMigrator;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProSales.Repository.Contexts;

// Migra os dados da Cime.Auth do SQL Server para o PostgreSQL (feature 0014).
// Conexões por variável de ambiente (nunca em arquivo): CIME_SOURCE_SQLSERVER e CIME_TARGET_POSTGRES.
// Nenhum valor de dado é impresso: só tabelas, colunas, chaves e contagens.
//
//   schema   aplica as migrações da auth no destino (cria o schema "auth")
//   check    só leitura: colunas compatíveis, valores que o destino recusaria, destino vazio
//   copy     copia tudo numa transação, com os ids originais, e ajusta as sequências
//   verify   compara origem e destino linha a linha (hash canônico); 0 diferenças = nada perdido
//   reset --confirm <database>   DROP SCHEMA auth CASCADE (para refazer depois de um ensaio)

var command = args.FirstOrDefault()?.ToLowerInvariant();
try
{
    return command switch
    {
        "schema" => await Commands.SchemaAsync(),
        "check" => await Commands.CheckAsync(requireEmptyTarget: false),
        "copy" => await Commands.CopyAsync(),
        "verify" => await Commands.VerifyAsync(),
        "reset" => await Commands.ResetAsync(args),
        _ => Usage(),
    };
}
catch (Exception e)
{
    Console.Error.WriteLine($"ERRO: {Db.Describe(e)}");
    return 2;
}

static int Usage()
{
    Console.WriteLine("uso: Cime.Auth.DataMigrator schema|check|copy|verify|reset --confirm <database>");
    Console.WriteLine("env: CIME_SOURCE_SQLSERVER (SQL Server de origem), CIME_TARGET_POSTGRES (Postgres de destino)");
    return 1;
}

static class Commands
{
    static string Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : throw new InvalidOperationException($"Defina a variável {name}.");

    static async Task<SqlConnection> SourceAsync()
    {
        var c = new SqlConnection(Env("CIME_SOURCE_SQLSERVER"));
        await c.OpenAsync();
        return c;
    }

    static async Task<NpgsqlConnection> TargetAsync()
    {
        var c = new NpgsqlConnection(Env("CIME_TARGET_POSTGRES"));
        await c.OpenAsync();
        return c;
    }

    public static async Task<int> SchemaAsync()
    {
        var options = new DbContextOptionsBuilder<DefaultContext>()
            .UseNpgsql(Env("CIME_TARGET_POSTGRES"), o => o.MigrationsHistoryTable(Tables.HistoryTable, Tables.TargetSchema))
            .Options;
        await using var context = new DefaultContext(options, null!);
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
        await context.Database.MigrateAsync();
        Console.WriteLine(pending.Count == 0
            ? "schema: nada a aplicar (já atualizado)."
            : $"schema: aplicadas {pending.Count} migração(ões): {string.Join(", ", pending)}");
        return 0;
    }

    /// <summary>Colunas de origem e destino por tabela; erros de compatibilidade vão para <paramref name="errors"/>.</summary>
    static async Task<Dictionary<string, (List<Column> Source, Dictionary<string, Column> Target)>> MapAsync(
        SqlConnection src, NpgsqlConnection dst, List<string> errors)
    {
        var map = new Dictionary<string, (List<Column>, Dictionary<string, Column>)>();
        foreach (var t in Tables.All)
        {
            var s = await Db.SourceColumnsAsync(src, t.Name);
            var d = await Db.TargetColumnsAsync(dst, t.Name);
            if (s.Count == 0) { errors.Add($"{t.Name}: não existe na origem"); continue; }
            if (d.Count == 0) { errors.Add($"{t.Name}: não existe no destino (rode \"schema\" antes)"); continue; }

            var sNames = s.Select(c => c.Name).ToHashSet();
            var dNames = d.Select(c => c.Name).ToHashSet();
            foreach (var c in sNames.Except(dNames)) errors.Add($"{t.Name}.{c}: existe na origem e não no destino (o dado seria perdido)");
            foreach (var c in dNames.Except(sNames)) errors.Add($"{t.Name}.{c}: existe no destino e não na origem");
            map[t.Name] = (s, d.ToDictionary(c => c.Name));
        }
        return map;
    }

    public static async Task<int> CheckAsync(bool requireEmptyTarget)
    {
        await using var src = await SourceAsync();
        await using var dst = await TargetAsync();
        Console.WriteLine($"origem : SQL Server {src.ServerVersion} / {src.Database}");
        Console.WriteLine($"destino: PostgreSQL {dst.PostgreSqlVersion} / {dst.Database} (schema {Tables.TargetSchema})");

        var errors = new List<string>();
        var warnings = new List<string>();
        var map = await MapAsync(src, dst, errors);

        foreach (var t in Tables.All)
        {
            if (!map.TryGetValue(t.Name, out var cols)) continue;
            long rows = 0;
            await using (var cmd = new SqlCommand(Db.SourceSelect(t, cols.Source.Select(c => c.Name)), src))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    rows++;
                    string? key = null;
                    string Key() => key ??= string.Join("|", t.Key.Select(k => Convert.ToString(r[k])));
                    foreach (var c in cols.Source)
                    {
                        if (r[c.Name] is not string s) continue;
                        var target = cols.Target[c.Name];
                        if (s.Contains('\0'))
                            errors.Add($"{t.Name}.{c.Name} (chave {Key()}): contém \\0, que o Postgres não aceita em texto");
                        if (target.MaxLength is { } max && Db.CodePoints(s) > max)
                            errors.Add($"{t.Name}.{c.Name} (chave {Key()}): {Db.CodePoints(s)} caracteres > {max} do destino");
                        if (Tables.LookupColumns.Contains(c.Name) && s.Length > 0 && (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])))
                            warnings.Add($"{t.Name}.{c.Name} (chave {Key()}): espaço no início/fim (o SQL Server ignora na comparação, o Postgres não)");
                    }
                }
            }

            await using var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {Tables.TargetSchema}.{Db.Q(t.Name)}", dst);
            var targetRows = (long)(await countCmd.ExecuteScalarAsync())!;
            Console.WriteLine($"  {t.Name,-18} origem {rows,6}   destino {targetRows,6}");
            if (requireEmptyTarget && targetRows > 0)
                errors.Add($"{t.Name}: o destino já tem {targetRows} linha(s); o copy exige destino vazio (use \"reset\")");
        }

        foreach (var w in warnings) Console.WriteLine($"AVISO: {w}");
        foreach (var e in errors) Console.WriteLine($"ERRO:  {e}");
        Console.WriteLine(errors.Count == 0 ? "check: ok" : $"check: {errors.Count} erro(s)");
        return errors.Count == 0 ? 0 : 1;
    }

    public static async Task<int> CopyAsync()
    {
        if (await CheckAsync(requireEmptyTarget: true) != 0)
        {
            Console.WriteLine("copy: abortado (corrija os erros do check).");
            return 1;
        }

        await using var src = await SourceAsync();
        await using var dst = await TargetAsync();
        var map = await MapAsync(src, dst, new List<string>());

        await using var tx = await dst.BeginTransactionAsync();
        foreach (var t in Tables.All)
        {
            var (sourceCols, targetCols) = map[t.Name];
            var names = sourceCols.Select(c => c.Name).ToList();
            var insert = $"INSERT INTO {Tables.TargetSchema}.{Db.Q(t.Name)} ({string.Join(", ", names.Select(Db.Q))}) " +
                         $"VALUES ({string.Join(", ", names.Select((_, i) => $"${i + 1}"))})";

            long rows = 0;
            await using var read = new SqlCommand(Db.SourceSelect(t, names), src);
            await using var r = await read.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                await using var cmd = new NpgsqlCommand(insert, dst, tx);
                foreach (var n in names)
                    cmd.Parameters.Add(new NpgsqlParameter { Value = Db.ToTarget(r[n], targetCols[n]) });
                await cmd.ExecuteNonQueryAsync();
                rows++;
            }

            // Ids explícitos não avançam a identity: a sequência passa para max(id).
            foreach (var id in targetCols.Values.Where(c => c.IsIdentity))
            {
                var table = $"{Tables.TargetSchema}.{Db.Q(t.Name)}";
                await using var seq = new NpgsqlCommand(
                    $"SELECT setval(pg_get_serial_sequence('{table}', '{id.Name}'), GREATEST(COALESCE(MAX({Db.Q(id.Name)}), 0), 1), MAX({Db.Q(id.Name)}) IS NOT NULL) FROM {table}",
                    dst, tx);
                await seq.ExecuteScalarAsync();
            }
            Console.WriteLine($"  {t.Name,-18} {rows,6} linha(s) copiadas");
        }
        await tx.CommitAsync();
        Console.WriteLine("copy: ok (transação confirmada). Rode \"verify\".");
        return 0;
    }

    public static async Task<int> VerifyAsync()
    {
        await using var src = await SourceAsync();
        await using var dst = await TargetAsync();
        var errors = new List<string>();
        var map = await MapAsync(src, dst, errors);
        var diffs = 0;

        foreach (var t in Tables.All)
        {
            if (!map.TryGetValue(t.Name, out var cols)) continue;
            var names = cols.Source.Select(c => c.Name).ToList();
            var source = await LoadAsync(new SqlCommand(Db.SourceSelect(t, names), src), t, names, cols.Target);
            var target = await LoadAsync(new NpgsqlCommand(Db.TargetSelect(t, names), dst), t, names, cols.Target);

            var missing = source.Keys.Except(target.Keys).ToList();
            var extra = target.Keys.Except(source.Keys).ToList();
            var changed = source.Keys.Intersect(target.Keys)
                .Select(k => (Key: k, Cols: names.Where(n => source[k][n] != target[k][n]).ToList()))
                .Where(x => x.Cols.Count > 0).ToList();

            var tableDiffs = missing.Count + extra.Count + changed.Count;
            diffs += tableDiffs;
            Console.WriteLine($"  {t.Name,-18} origem {source.Count,6}   destino {target.Count,6}   " +
                              (tableDiffs == 0 ? "idêntico" : $"faltando {missing.Count}, sobrando {extra.Count}, divergentes {changed.Count}"));
            foreach (var k in missing.Take(20)) Console.WriteLine($"      faltando no destino: chave {k}");
            foreach (var k in extra.Take(20)) Console.WriteLine($"      só no destino:       chave {k}");
            foreach (var (k, c) in changed.Take(20)) Console.WriteLine($"      divergente:          chave {k} → {string.Join(", ", c)}");
        }

        foreach (var e in errors) Console.WriteLine($"ERRO:  {e}");
        var ok = diffs == 0 && errors.Count == 0;
        Console.WriteLine(ok ? "verify: 0 diferenças — nenhum dado perdido." : $"verify: {diffs} diferença(s), {errors.Count} erro(s) de estrutura.");
        return ok ? 0 : 1;
    }

    static async Task<Dictionary<string, Dictionary<string, string>>> LoadAsync(
        System.Data.Common.DbCommand cmd, TableSpec t, List<string> names, Dictionary<string, Column> target)
    {
        var rows = new Dictionary<string, Dictionary<string, string>>();
        await using (cmd)
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var row = names.ToDictionary(n => n, n => Db.Canonical(r[n], target[n]));
                rows[string.Join("|", t.Key.Select(k => row[k]))] = row;
            }
        }
        return rows;
    }

    public static async Task<int> ResetAsync(string[] args)
    {
        var i = Array.IndexOf(args, "--confirm");
        var confirm = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;

        await using var dst = await TargetAsync();
        if (confirm != dst.Database)
        {
            Console.WriteLine($"reset: recusado. Confirme com --confirm {dst.Database} (apaga o schema {Tables.TargetSchema} inteiro).");
            return 1;
        }

        var known = Tables.All.Select(t => t.Name).Append(Tables.HistoryTable).ToHashSet();
        var existing = new List<string>();
        await using (var cmd = new NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema = @s", dst))
        {
            cmd.Parameters.AddWithValue("s", Tables.TargetSchema);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) existing.Add(r.GetString(0));
        }
        var unknown = existing.Where(n => !known.Contains(n)).ToList();
        if (unknown.Count > 0)
        {
            Console.WriteLine($"reset: recusado. O schema {Tables.TargetSchema} tem tabelas que não são da auth: {string.Join(", ", unknown)}");
            return 1;
        }

        await using (var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {Tables.TargetSchema} CASCADE", dst))
            await drop.ExecuteNonQueryAsync();
        Console.WriteLine($"reset: schema {Tables.TargetSchema} removido ({existing.Count} tabela(s)).");
        return 0;
    }
}
