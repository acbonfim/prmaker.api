using Cime.DataMigrator;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Migra dados para o PostgreSQL, por perfil (features 0014 e 0015):
//   auth    SQL Server (dbo)      → schema auth
//   prform  MySQL (db31021)       → schemas prform, vacations, timeline
// Conexões por variável de ambiente (nunca em arquivo): CIME_SOURCE e CIME_TARGET_POSTGRES.
// Nenhum valor de dado é impresso: só tabelas, colunas, chaves e contagens.
//
//   <perfil> schema   aplica as migrações dos contextos do perfil no destino
//   <perfil> check    só leitura: colunas compatíveis, valores que o destino recusaria, destino vazio
//   <perfil> copy     copia tudo numa transação, com os ids originais, e ajusta as sequências
//   <perfil> verify   compara origem e destino linha a linha (hash canônico); 0 diferenças = nada perdido
//   <perfil> reset --confirm <database>   DROP dos schemas do perfil (nunca toca nos de outro perfil)

if (args.Length < 2) return Usage();
try
{
    var profile = Profile.Get(args[0]);
    var commands = new Commands(profile);
    return args[1].ToLowerInvariant() switch
    {
        "schema" => await commands.SchemaAsync(),
        "check" => await commands.CheckAsync(requireEmptyTarget: false),
        "copy" => await commands.CopyAsync(),
        "verify" => await commands.VerifyAsync(),
        "reset" => await commands.ResetAsync(args),
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
    Console.WriteLine("uso: Cime.DataMigrator auth|prform schema|check|copy|verify|reset --confirm <database>");
    Console.WriteLine("env: CIME_SOURCE (banco de origem), CIME_TARGET_POSTGRES (PostgreSQL de destino)");
    return 1;
}

class Commands(Profile profile)
{
    static string Env(params string[] names) =>
        names.Select(Environment.GetEnvironmentVariable).FirstOrDefault(v => !string.IsNullOrEmpty(v))
        ?? throw new InvalidOperationException($"Defina a variável {names[0]}.");

    // CIME_SOURCE_SQLSERVER: nome usado na 0014, aceito por compatibilidade.
    ISource Source() => profile.CreateSource(Env("CIME_SOURCE", "CIME_SOURCE_SQLSERVER"));
    static string TargetConnection => Env("CIME_TARGET_POSTGRES");

    static async Task<NpgsqlConnection> TargetAsync()
    {
        var c = new NpgsqlConnection(TargetConnection);
        await c.OpenAsync();
        return c;
    }

    public async Task<int> SchemaAsync()
    {
        foreach (var context in profile.CreateContexts(TargetConnection))
        {
            await using (context)
            {
                var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
                await context.Database.MigrateAsync();
                Console.WriteLine($"schema {context.GetType().Name}: " + (pending.Count == 0
                    ? "nada a aplicar (já atualizado)."
                    : $"aplicadas {pending.Count} migração(ões): {string.Join(", ", pending)}"));
            }
        }
        return 0;
    }

    /// <summary>Colunas de origem e destino por tabela; erros de compatibilidade vão para <paramref name="errors"/>.</summary>
    async Task<Dictionary<TableSpec, (List<Column> Source, Dictionary<string, Column> Target)>> MapAsync(
        ISource src, NpgsqlConnection dst, List<TableSpec> tables, List<string> errors)
    {
        var map = new Dictionary<TableSpec, (List<Column>, Dictionary<string, Column>)>();
        foreach (var t in tables)
        {
            var s = await src.ColumnsAsync(t.Name);
            var d = await Db.TargetColumnsAsync(dst, t.Schema, t.Name);
            if (s.Count == 0) { errors.Add($"{t.Name}: não existe na origem"); continue; }
            if (d.Count == 0) { errors.Add($"{t.Schema}.{t.Name}: não existe no destino (rode \"schema\" antes)"); continue; }

            var sNames = s.Select(c => c.Name).ToHashSet();
            var dNames = d.Select(c => c.Name).ToHashSet();
            foreach (var c in sNames.Except(dNames)) errors.Add($"{t.Name}.{c}: existe na origem e não no destino (o dado seria perdido)");
            foreach (var c in dNames.Except(sNames)) errors.Add($"{t.Name}.{c}: existe no destino e não na origem");
            map[t] = (s, d.ToDictionary(c => c.Name));
        }
        return map;
    }

    public async Task<int> CheckAsync(bool requireEmptyTarget)
    {
        await using var src = Source();
        await using var dst = await TargetAsync();
        var tables = profile.Tables(TargetConnection);
        Console.WriteLine($"perfil : {profile.Name} ({tables.Count} tabelas em {string.Join(", ", profile.Schemas)})");
        Console.WriteLine($"origem : {src.Describe()}");
        Console.WriteLine($"destino: PostgreSQL {dst.PostgreSqlVersion} / {dst.Database}");

        var errors = new List<string>();
        var warnings = await src.NotesAsync();
        var map = await MapAsync(src, dst, tables, errors);

        foreach (var t in tables)
        {
            if (!map.TryGetValue(t, out var cols)) continue;
            long rows = 0;
            await using (var cmd = src.Connection.CreateCommand())
            {
                cmd.CommandText = src.Select(t, cols.Source.Select(c => c.Name));
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    rows++;
                    string? key = null;
                    string Key() => key ??= string.Join("|", t.Key.Select(k => Convert.ToString(r[k])));
                    foreach (var c in cols.Source)
                    {
                        var value = r.GetValue(r.GetOrdinal(c.Name));
                        if (Db.IsInvalidDate(value))
                            errors.Add($"{t.Name}.{c.Name} (chave {Key()}): data zerada ('0000-00-00'), que o Postgres não aceita");
                        if (value is not string s) continue;
                        var target = cols.Target[c.Name];
                        if (s.Contains('\0'))
                            errors.Add($"{t.Name}.{c.Name} (chave {Key()}): contém \\0, que o Postgres não aceita em texto");
                        if (target.MaxLength is { } max && Db.CodePoints(s) > max)
                            errors.Add($"{t.Name}.{c.Name} (chave {Key()}): {Db.CodePoints(s)} caracteres > {max} do destino");
                        if (profile.LookupColumns.Contains(c.Name) && s.Length > 0 && (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])))
                            warnings.Add($"{t.Name}.{c.Name} (chave {Key()}): espaço no início/fim (a origem pode ignorar na comparação, o Postgres não)");
                    }
                }
            }

            await using var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {t.Target}", dst);
            var targetRows = (long)(await countCmd.ExecuteScalarAsync())!;
            Console.WriteLine($"  {t.Schema + "." + t.Name,-36} origem {rows,7}   destino {targetRows,7}");
            if (requireEmptyTarget && targetRows > 0)
                errors.Add($"{t.Schema}.{t.Name}: o destino já tem {targetRows} linha(s); o copy exige destino vazio (use \"reset\")");
        }

        foreach (var w in warnings) Console.WriteLine($"AVISO: {w}");
        foreach (var e in errors) Console.WriteLine($"ERRO:  {e}");
        Console.WriteLine(errors.Count == 0 ? "check: ok" : $"check: {errors.Count} erro(s)");
        return errors.Count == 0 ? 0 : 1;
    }

    public async Task<int> CopyAsync()
    {
        if (await CheckAsync(requireEmptyTarget: true) != 0)
        {
            Console.WriteLine("copy: abortado (corrija os erros do check).");
            return 1;
        }

        await using var src = Source();
        await using var dst = await TargetAsync();
        var tables = profile.Tables(TargetConnection);
        var map = await MapAsync(src, dst, tables, new List<string>());

        await using var tx = await dst.BeginTransactionAsync();
        foreach (var t in tables)
        {
            var (sourceCols, targetCols) = map[t];
            var names = sourceCols.Select(c => c.Name).ToList();
            var insert = $"INSERT INTO {t.Target} ({string.Join(", ", names.Select(Db.Q))}) " +
                         $"VALUES ({string.Join(", ", names.Select((_, i) => $"${i + 1}"))})";

            long rows = 0;
            await using (var read = src.Connection.CreateCommand())
            {
                read.CommandText = src.Select(t, names);
                await using var r = await read.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    await using var cmd = new NpgsqlCommand(insert, dst, tx);
                    foreach (var n in names)
                        cmd.Parameters.Add(new NpgsqlParameter { Value = Db.ToTarget(r.GetValue(r.GetOrdinal(n)), targetCols[n]) });
                    await cmd.ExecuteNonQueryAsync();
                    rows++;
                }
            }

            // Ids explícitos não avançam a identity: a sequência passa para max(id).
            foreach (var id in targetCols.Values.Where(c => c.IsIdentity))
            {
                await using var seq = new NpgsqlCommand(
                    $"SELECT setval(pg_get_serial_sequence('{t.Target}', '{id.Name}'), GREATEST(COALESCE(MAX({Db.Q(id.Name)}), 0), 1), MAX({Db.Q(id.Name)}) IS NOT NULL) FROM {t.Target}",
                    dst, tx);
                await seq.ExecuteScalarAsync();
            }
            Console.WriteLine($"  {t.Schema + "." + t.Name,-36} {rows,7} linha(s) copiadas");
        }
        await tx.CommitAsync();
        Console.WriteLine("copy: ok (transação confirmada). Rode \"verify\".");
        return 0;
    }

    public async Task<int> VerifyAsync()
    {
        await using var src = Source();
        await using var dst = await TargetAsync();
        var tables = profile.Tables(TargetConnection);
        var errors = new List<string>();
        var map = await MapAsync(src, dst, tables, errors);
        var diffs = 0;

        foreach (var t in tables)
        {
            if (!map.TryGetValue(t, out var cols)) continue;
            var names = cols.Source.Select(c => c.Name).ToList();
            var sourceCmd = src.Connection.CreateCommand();
            sourceCmd.CommandText = src.Select(t, names);
            var source = await LoadAsync(sourceCmd, t, names, cols.Target);
            var target = await LoadAsync(new NpgsqlCommand(Db.TargetSelect(t, names), dst), t, names, cols.Target);

            var missing = source.Keys.Except(target.Keys).ToList();
            var extra = target.Keys.Except(source.Keys).ToList();
            var changed = source.Keys.Intersect(target.Keys)
                .Select(k => (Key: k, Cols: names.Where(n => source[k][n] != target[k][n]).ToList()))
                .Where(x => x.Cols.Count > 0).ToList();

            var tableDiffs = missing.Count + extra.Count + changed.Count;
            diffs += tableDiffs;
            Console.WriteLine($"  {t.Schema + "." + t.Name,-36} origem {source.Count,7}   destino {target.Count,7}   " +
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
                var row = names.ToDictionary(n => n, n => Db.Canonical(r.GetValue(r.GetOrdinal(n)), target[n]));
                rows[string.Join("|", t.Key.Select(k => row[k]))] = row;
            }
        }
        return rows;
    }

    public async Task<int> ResetAsync(string[] args)
    {
        var i = Array.IndexOf(args, "--confirm");
        var confirm = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;

        await using var dst = await TargetAsync();
        var schemas = profile.Schemas.ToList();
        if (confirm != dst.Database)
        {
            Console.WriteLine($"reset: recusado. Confirme com --confirm {dst.Database} (apaga os schemas {string.Join(", ", schemas)} inteiros).");
            return 1;
        }

        var known = profile.Tables(TargetConnection).Select(t => (t.Schema, t.Name))
            .Concat(schemas.Select(s => (s, Profile.HistoryTable))).ToHashSet();
        var existing = new List<(string Schema, string Name)>();
        await using (var cmd = new NpgsqlCommand("SELECT table_schema, table_name FROM information_schema.tables WHERE table_schema = ANY(@s)", dst))
        {
            cmd.Parameters.AddWithValue("s", schemas.ToArray());
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) existing.Add((r.GetString(0), r.GetString(1)));
        }
        var unknown = existing.Where(e => !known.Contains(e)).ToList();
        if (unknown.Count > 0)
        {
            Console.WriteLine($"reset: recusado. Há tabelas que não são do perfil {profile.Name}: {string.Join(", ", unknown.Select(u => $"{u.Schema}.{u.Name}"))}");
            return 1;
        }

        foreach (var schema in schemas)
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {Db.Q(schema)} CASCADE", dst);
            await drop.ExecuteNonQueryAsync();
        }
        Console.WriteLine($"reset: schemas {string.Join(", ", schemas)} removidos ({existing.Count} tabela(s)).");
        return 0;
    }
}
