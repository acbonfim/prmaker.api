using System.Text.RegularExpressions;

namespace solvace.governance.domain.Standards;

/// <summary>Um achado de conferência: qual verificação, linha (1-based), trecho e o que fazer.</summary>
public sealed record CheckHit(string Check, int? Line, string Excerpt, string Hint);

/// <summary>
/// 0101: conferências embutidas dos padrões de banco (wiki "Data Nomenclature and Rules", SQL Server) sobre o TEXTO de um
/// script SQL ou de uma migração do EF Core — o que a skill grava ou o Claude edita. Heurísticas por linha/instrução,
/// sem parser completo de T-SQL: preferem deixar passar a acusar à toa. Objetos no <c>dbo</c> (legado) e a tabela de
/// histórico do EF ficam fora das regras de nome (G8, S5, T3, P4, V5).
/// </summary>
public static partial class SqlStandardChecks
{
    public const string UnnamedConstraint = "unnamed-constraint";
    public const string UnqualifiedObject = "unqualified-object";
    public const string EfCascade = "ef-cascade";
    public const string EfUnnamedDefault = "ef-unnamed-default";
    public const string ObjectUnderscore = "object-underscore";
    public const string ProcedurePrefix = "procedure-prefix";
    public const string ViewPrefix = "view-prefix";
    public const string SelectStar = "select-star";
    public const string MoneyType = "money-type";
    public const string SchemaName = "schema-name";
    public const string NewObjectInDbo = "new-object-in-dbo";
    public const string TablePrefix = "table-prefix";
    public const string TableName = "table-name";
    public const string PkName = "pk-name";
    public const string FkName = "fk-name";
    public const string Cascade = "cascade";
    public const string NoCheck = "nocheck";
    public const string IndexName = "index-name";
    public const string DefaultName = "default-name";
    public const string CheckName = "check-name";
    public const string CreateOrAlterProcedure = "create-or-alter-procedure";

    public static readonly string[] All =
    [
        UnnamedConstraint, UnqualifiedObject, EfCascade, EfUnnamedDefault, ObjectUnderscore, ProcedurePrefix, ViewPrefix,
        SelectStar, MoneyType, SchemaName, NewObjectInDbo, TablePrefix, TableName, PkName, FkName, Cascade, NoCheck,
        IndexName, DefaultName, CheckName, CreateOrAlterProcedure
    ];

    private const string Name = @"(?:\[?(?<s>[A-Za-z_][\w]*)\]?\s*\.\s*)?\[?(?<n>[A-Za-z_#@][\w$#@]*)\]?";
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"\bCREATE\s+(?:OR\s+ALTER\s+)?(?<kind>TABLE|VIEW|PROC|PROCEDURE|FUNCTION)\s+" + Name, Opt)]
    private static partial Regex CreateObject();

    [GeneratedRegex(@"\b(?:CREATE|ALTER)\s+TABLE\s+" + Name, Opt)]
    private static partial Regex TableContext();

    [GeneratedRegex(@"\bCREATE\s+SCHEMA\s+\[?(?<n>[^\s\];]+)\]?", Opt)]
    private static partial Regex CreateSchema();

    [GeneratedRegex(@"\bCREATE\s+(?<u>UNIQUE\s+)?(?:CLUSTERED\s+|NONCLUSTERED\s+)?INDEX\s+\[?(?<i>[\w$#]+)\]?\s+ON\s+" + Name, Opt)]
    private static partial Regex CreateIndex();

    [GeneratedRegex(@"\bCONSTRAINT\s+\[?(?<c>[\w$#]+)\]?\s+(?<kind>PRIMARY\s+KEY|FOREIGN\s+KEY|DEFAULT|CHECK|UNIQUE)", Opt)]
    private static partial Regex NamedConstraint();

    [GeneratedRegex(@"\bCONSTRAINT\s+\[?[\w$#]+\]?\s*$", Opt)]
    private static partial Regex DanglingConstraintName();

    [GeneratedRegex(@"\b(PRIMARY\s+KEY|DEFAULT\b|CHECK\s*\(|REFERENCES\b|UNIQUE\b)", Opt)]
    private static partial Regex ConstraintKeyword();

    [GeneratedRegex(@"^\s*\[?(?<col>[A-Za-z_][\w]*)\]?\s+(?<type>\[?[A-Za-z]+\]?)", Opt)]
    private static partial Regex ColumnDefinition();

    [GeneratedRegex(@"\bON\s+(?<what>DELETE|UPDATE)\s+CASCADE\b", Opt)]
    private static partial Regex CascadeClause();

    [GeneratedRegex(@"\bWITH\s+NOCHECK\b|\bNOCHECK\s+CONSTRAINT\b", Opt)]
    private static partial Regex NoCheckClause();

    [GeneratedRegex(@"\bSELECT\s+(?:DISTINCT\s+|TOP\s*\(?\s*\d+\s*\)?\s+)?(?:[\w\[\]]+\.)?\*", Opt)]
    private static partial Regex SelectStarClause();

    [GeneratedRegex(@"\b(?:SMALL)?MONEY\b|HasColumnType\(\s*""(?:small)?money""", Opt)]
    private static partial Regex Money();

    [GeneratedRegex(@"DeleteBehavior\.Cascade|onDelete:\s*ReferentialAction\.Cascade|onUpdate:\s*ReferentialAction\.Cascade")]
    private static partial Regex EfCascadeCode();

    [GeneratedRegex(@"\.HasDefaultValue(?:Sql)?\(|\bdefaultValue(?:Sql)?:\s*(?!null\b)")]
    private static partial Regex EfDefault();

    [GeneratedRegex(@"CreateIndex\(\s*name:\s*""(?<i>[^""]+)""\s*,\s*(?:schema:\s*""(?<s>[^""]+)""\s*,\s*)?table:\s*""(?<n>[^""]+)""", RegexOptions.Singleline)]
    private static partial Regex EfCreateIndex();

    [GeneratedRegex(@"\bFOREIGN\s+KEY\s*\((?<cols>[^)]*)\)", Opt)]
    private static partial Regex ForeignKeyColumns();

    [GeneratedRegex(@"\bDEFAULT\b.*?\bFOR\s+\[?(?<col>\w+)\]?", Opt)]
    private static partial Regex DefaultFor();

    [GeneratedRegex(@"\bCOUNT\s*\(\s*\*\s*\)|\bEXISTS\s*\(\s*SELECT\s+\*", Opt)]
    private static partial Regex HarmlessStar();

    private static readonly HashSet<string> SqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "bigint", "smallint", "tinyint", "bit", "decimal", "numeric", "money", "smallmoney", "float", "real", "date",
        "datetime", "datetime2", "smalldatetime", "datetimeoffset", "time", "char", "varchar", "nchar", "nvarchar", "text",
        "ntext", "binary", "varbinary", "image", "uniqueidentifier", "xml", "sql_variant", "geography", "geometry", "hierarchyid", "rowversion", "timestamp"
    };

    private sealed record Ctx(string? Schema, string? Table);

    /// <summary>Roda as conferências pedidas sobre o texto. <paramref name="target"/>: sql | migration | code.</summary>
    public static List<CheckHit> Run(string text, IReadOnlyCollection<string> checks, string target = "sql")
    {
        var hits = new List<CheckHit>();
        if (string.IsNullOrWhiteSpace(text) || checks.Count == 0) return hits;
        var want = checks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var isDefinition = CreateObject().IsMatch(text) || target is "migration" or "code";

        Ctx ctx = new(null, null);
        var prev = string.Empty;
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = StripComment(raw);
            if (line.Trim().Length == 0) { continue; }
            var no = i + 1;
            void Hit(string check, string hint)
            {
                if (want.Contains(check)) hits.Add(new CheckHit(check, no, Excerpt(raw), hint));
            }

            if (TableContext().Match(line) is { Success: true } tc) ctx = new(Schema(tc), tc.Groups["n"].Value);
            var custom = ctx.Schema is not null && !IsLegacy(ctx.Schema, ctx.Table);

            // Criação de objetos (tabela, view, procedure, função).
            foreach (Match m in CreateObject().Matches(line))
            {
                var kind = m.Groups["kind"].Value.ToUpperInvariant();
                var schema = Schema(m);
                var name = m.Groups["n"].Value;
                if (name.StartsWith('#') || name.StartsWith('@')) continue;
                if (schema is null) Hit(UnqualifiedObject, $"Crie com o schema do módulo: <schema>.{name} (G6).");
                else if (schema.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                    Hit(NewObjectInDbo, $"Objeto novo não vai para o dbo: use o schema do módulo (S5) — dbo.{name} só se for legado.");
                var legacy = schema is null || IsLegacy(schema, name);
                if (kind == "TABLE")
                {
                    if (Regex.IsMatch(name, "^(TB_|TBL|TB_MST_)", RegexOptions.IgnoreCase))
                        Hit(TablePrefix, $"Tabela nova sem prefixo TB_/tbl (T4): {name}.");
                    if (!legacy && !Regex.IsMatch(name, "^[A-Z][A-Za-z0-9]*$"))
                        Hit(TableName, $"Nome de tabela em PascalCase, sem _ nem caracteres especiais (T1): {name}.");
                    if (!legacy && name.Contains('_')) Hit(ObjectUnderscore, $"Sem _ no nome (N4): {name}.");
                }
                else if (kind is "PROC" or "PROCEDURE")
                {
                    if (Regex.IsMatch(name, "^sp_?", RegexOptions.IgnoreCase))
                        Hit(ProcedurePrefix, $"Procedure com prefixo pr, nunca sp/sp_ (N14, P5): {schema ?? "<schema>"}.pr{Regex.Replace(name, "^sp_?", "", RegexOptions.IgnoreCase)}.");
                    else if (!legacy && !Regex.IsMatch(name, "^pr[A-Z][A-Za-z0-9]*$"))
                        Hit(ProcedurePrefix, $"Procedure no padrão [schema].pr[Nome] (P2): {name}.");
                    if (!Regex.IsMatch(line, @"\bOR\s+ALTER\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(line, @"^\s*ALTER\b", RegexOptions.IgnoreCase))
                        Hit(CreateOrAlterProcedure, "Use CREATE OR ALTER PROCEDURE nos scripts de deploy (P7).");
                }
                else if (kind == "VIEW")
                {
                    if (!legacy && !Regex.IsMatch(name, "^vw[A-Z][A-Za-z0-9]*$"))
                        Hit(ViewPrefix, $"View no padrão [schema].vw[Nome] (V2): {name}.");
                    if (!legacy && name.Contains('_')) Hit(ObjectUnderscore, $"Sem _ no nome da view (V3): {name}.");
                }
            }

            if (CreateSchema().Match(line) is { Success: true } cs)
            {
                var s = cs.Groups["n"].Value;
                if (!Regex.IsMatch(s, "^[a-z][a-zA-Z0-9]*$"))
                    Hit(SchemaName, $"Schema em camelCase: palavra única minúscula, composto como actionPlan (S1–S3): {s}.");
            }

            // Índices.
            if (CreateIndex().Match(line) is { Success: true } ci && Schema(ci) is { } ixSchema && !IsLegacy(ixSchema, ci.Groups["n"].Value))
                IndexHit(ci.Groups["i"].Value, ixSchema, ci.Groups["n"].Value, ci.Groups["u"].Success, Hit);

            // Constraints nomeadas.
            foreach (Match nc in NamedConstraint().Matches(line))
            {
                if (!custom) continue;
                var c = nc.Groups["c"].Value;
                var kind = Regex.Replace(nc.Groups["kind"].Value.ToUpperInvariant(), @"\s+", " ");
                var s = ctx.Schema!;
                var t = ctx.Table!;
                switch (kind)
                {
                    case "PRIMARY KEY" when !c.Equals($"PK_{s}_{t}", StringComparison.Ordinal):
                        Hit(PkName, $"PK nomeada PK_{s}_{t} (PK3), não {c}.");
                        break;
                    case "FOREIGN KEY":
                        if (ForeignKeyColumns().Match(line, nc.Index) is { Success: true } fk)
                        {
                            var cols = fk.Groups["cols"].Value.Split(',').Select(x => x.Trim().Trim('[', ']')).Where(x => x.Length > 0).ToList();
                            var main = cols.FirstOrDefault(x => !x.Equals("SiteId", StringComparison.OrdinalIgnoreCase)) ?? cols.FirstOrDefault();
                            if (main is not null && !c.Equals($"FK_{s}_{t}_{main}", StringComparison.Ordinal))
                                Hit(FkName, $"FK nomeada FK_{s}_{t}_{main} — tabela de origem + coluna principal, sem SiteId (FK3, FK4), não {c}.");
                        }
                        break;
                    case "DEFAULT":
                        var col = DefaultFor().Match(line, nc.Index) is { Success: true } df ? df.Groups["col"].Value
                            : ColumnDefinition().Match(line) is { Success: true } cd && !cd.Groups["col"].Value.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ? cd.Groups["col"].Value : null;
                        if (col is not null && !c.Equals($"DF_{s}_{t}_{col}", StringComparison.Ordinal))
                            Hit(DefaultName, $"Default nomeado DF_{s}_{t}_{col} (DF1), não {c}.");
                        break;
                    case "CHECK" when !c.StartsWith($"CK_{s}_{t}_", StringComparison.Ordinal):
                        Hit(CheckName, $"Check nomeada CK_{s}_{t}_<Coluna ou Regra> (CK1, CK2), não {c}.");
                        break;
                    case "UNIQUE":
                        Hit(IndexName, $"Unicidade de negócio com CREATE UNIQUE NONCLUSTERED INDEX IXU_{s}_{t}_NN em vez de constraint UNIQUE (IXU3).");
                        break;
                }
            }

            // Constraint sem nome (o SQL Server gera um nome diferente em cada ambiente).
            if (custom && ConstraintKeyword().IsMatch(line) && !Regex.IsMatch(line, @"\bCONSTRAINT\b", RegexOptions.IgnoreCase)
                && !DanglingConstraintName().IsMatch(prev) && !Regex.IsMatch(line, @"\bON\s+(DELETE|UPDATE)\b", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(line, @"^\s*(REFERENCES|DEFAULT\s+VALUES)", RegexOptions.IgnoreCase) && !CreateIndex().IsMatch(line))
                Hit(UnnamedConstraint, "Nomeie a constraint (CONSTRAINT PK_/FK_/DF_/CK_…) — sem nome gerado pelo sistema (G4, DF2, CK3, T7).");

            // Colunas.
            if (custom && ColumnDefinition().Match(line) is { Success: true } coldef && SqlTypes.Contains(coldef.Groups["type"].Value.Trim('[', ']'))
                && coldef.Groups["col"].Value.Contains('_'))
                Hit(ObjectUnderscore, $"Coluna sem _ no nome (N4): {coldef.Groups["col"].Value}.");
            if (Money().IsMatch(line) && (custom || target is "migration" or "code"))
                Hit(MoneyType, "Sem MONEY/SMALLMONEY: use DECIMAL(p, s) com a precisão do negócio (C2).");

            if (CascadeClause().Match(line) is { Success: true } cc)
                Hit(Cascade, $"Sem ON {cc.Groups["what"].Value.ToUpperInvariant()} CASCADE: exclusões/alterações nos filhos são explícitas (FK5, FK6, R5).");
            if (NoCheckClause().IsMatch(line))
                Hit(NoCheck, "FK/CHECK criadas WITH CHECK e confiáveis; depois de carga com NOCHECK, revalide (FK9, R6, CK5).");
            if (isDefinition && SelectStarClause().IsMatch(HarmlessStar().Replace(line, " ")))
                Hit(SelectStar, "Liste as colunas em vez de SELECT * (C1, V6).");

            // EF Core.
            if (EfCascadeCode().IsMatch(line))
                Hit(EfCascade, "Migrations do EF: DeleteBehavior.NoAction/Restrict, sem cascade (G7).");
            if (target is "migration" or "code" && EfDefault().IsMatch(line))
                Hit(EfUnnamedDefault, "O EF cria o default sem nome: nomeie (DF_[schema]_[Tabela]_[Coluna]) com SQL na migração (DF5).");

            prev = line;
        }

        // CreateIndex do EF (pode ocupar várias linhas).
        if (want.Contains(IndexName))
            foreach (Match m in EfCreateIndex().Matches(text))
            {
                var s = m.Groups["s"].Success ? m.Groups["s"].Value : null;
                if (s is null || IsLegacy(s, m.Groups["n"].Value)) continue;
                var unique = Regex.IsMatch(text.Substring(m.Index, Math.Min(400, text.Length - m.Index)), @"unique:\s*true");
                var line = text[..m.Index].Count(ch => ch == '\n') + 1;
                IndexHit(m.Groups["i"].Value, s, m.Groups["n"].Value, unique, (check, hint) => hits.Add(new CheckHit(check, line, Excerpt(m.Value), hint)));
            }

        return hits.GroupBy(h => (h.Check, h.Line, h.Hint)).Select(g => g.First()).ToList();
    }

    private static void IndexHit(string index, string schema, string table, bool unique, Action<string, string> hit)
    {
        var prefix = unique ? "IXU" : "IX";
        if (!Regex.IsMatch(index, $"^{prefix}_{Regex.Escape(schema)}_{Regex.Escape(table)}_\\d{{2}}$"))
            hit(IndexName, $"Índice{(unique ? " único" : "")} nomeado {prefix}_{schema}_{table}_NN (sequencial de 2 dígitos, sem colunas no nome — IX3, IX4{(unique ? ", IXU1" : "")}), não {index}.");
    }

    private static string? Schema(Match m) => m.Groups["s"].Success && m.Groups["s"].Value.Length > 0 ? m.Groups["s"].Value : null;

    /// <summary>Legado (dbo) e histórico do EF ficam fora das regras de nome.</summary>
    public static bool IsLegacy(string? schema, string? name) =>
        schema is null || schema.Equals("dbo", StringComparison.OrdinalIgnoreCase)
                       || (name?.StartsWith("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase) ?? false)
                       || (name?.StartsWith("__EFMigrationHistory", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string StripComment(string line)
    {
        var idx = line.IndexOf("--", StringComparison.Ordinal);
        if (idx >= 0) line = line[..idx];
        var slash = line.IndexOf("//", StringComparison.Ordinal);
        return slash >= 0 && !line.Contains("http") ? line[..slash] : line;
    }

    private static string Excerpt(string line)
    {
        var t = line.Trim();
        return t.Length <= 160 ? t : t[..157] + "…";
    }
}
