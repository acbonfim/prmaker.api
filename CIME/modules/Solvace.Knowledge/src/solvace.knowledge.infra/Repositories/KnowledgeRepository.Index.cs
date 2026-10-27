using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.infra.Contexts;

namespace solvace.knowledge.infra.Repositories;

/// <summary>
/// 0073: consultas ao índice da engenharia reversa feitas no PostgreSQL. Nenhuma lê a tabela inteira para a memória da API: cada uma
/// traz só as linhas (e só as colunas) de que precisa. As colunas <c>SearchText</c> e <c>HeadWords</c> são geradas pelo banco
/// (migração <c>SearchText0073</c>) — texto sem acento/caixa para o <c>LIKE '%termo%'</c> da busca.
/// </summary>
public partial class KnowledgeRepository
{
    private static readonly string IndexTable = $"\"{KnowledgeContext.Schema}\".\"ReverseIndexEntries\"";
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Linha lida do banco: as listas jsonb chegam como texto (<c>::text</c>) e viram listas aqui.</summary>
    private sealed class IndexRow
    {
        public Guid Id { get; set; }
        public string ModuleKey { get; set; } = "";
        public string DocType { get; set; } = "";
        public string ItemId { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Title { get; set; } = "";
        public int Level { get; set; }
        public int Order { get; set; }
        public string Body { get; set; } = "";
        public string Tags { get; set; } = "[]";
        public string Tables { get; set; } = "[]";
        public string Refs { get; set; } = "[]";
        public string Evidence { get; set; } = "[]";
        public string Modules { get; set; } = "[]";
        public string Synonyms { get; set; } = "[]";
        public bool Removed { get; set; }
        public int SectionVersion { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public ReverseIndexEntry ToEntry() => ReverseIndexEntry.Light(Id, ModuleKey, DocType, ItemId, Kind, Title, Level, Order, Body, List(Tags), List(Tables),
            List(Refs), List(Evidence), List(Modules), List(Synonyms), Removed, SectionVersion, UpdatedAt);

        private static List<string> List(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    private static string IndexColumns(ReverseBodyMode body) => $"""
        e."Id", e."ModuleKey", e."DocType", e."ItemId", e."Kind", e."Title", e."Level", e."Order",
        {body switch { ReverseBodyMode.None => "''::text", ReverseBodyMode.Preview => "left(e.\"Body\", 3000)", _ => "e.\"Body\"" }} AS "Body",
        {(body == ReverseBodyMode.Complete ? "e.\"Evidence\"::text" : "'[]'::text")} AS "Evidence",
        e."Tags"::text AS "Tags", e."Tables"::text AS "Tables", e."Refs"::text AS "Refs", e."Modules"::text AS "Modules", e."Synonyms"::text AS "Synonyms",
        e."Removed", e."SectionVersion", e."UpdatedAt"
        """;

    /// <summary>Acumula condições e parâmetros nomeados (<c>@p0</c>, <c>@p1</c>…) de uma consulta.</summary>
    private sealed class Sql
    {
        public readonly List<string> Where = [];
        public readonly List<NpgsqlParameter> Parameters = [];

        public string Param(object value)
        {
            var name = $"p{Parameters.Count}";
            Parameters.Add(new NpgsqlParameter(name, value));
            return "@" + name;
        }

        public string WhereClause => Where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", Where.Select(w => $"({w})"));
        public object[] Args => Parameters.Cast<object>().ToArray();
    }

    private async Task<List<ReverseIndexEntry>> RunIndexAsync(string sql, Sql q, CancellationToken cancellationToken) =>
        (await context.Database.SqlQueryRaw<IndexRow>(sql, q.Args).ToListAsync(cancellationToken)).Select(r => r.ToEntry()).ToList();

    private const string DefaultOrder = "e.\"ModuleKey\", e.\"DocType\", e.\"Order\"";

    public Task<List<ReverseIndexEntry>> QueryIndexAsync(ReverseIndexQuery query, CancellationToken cancellationToken)
    {
        var q = new Sql();
        if (query.Modules is { Count: > 0 }) q.Where.Add($"e.\"ModuleKey\" = ANY({q.Param(query.Modules.ToArray())})");
        if (query.Kinds is { Count: > 0 }) q.Where.Add($"e.\"Kind\" = ANY({q.Param(query.Kinds.ToArray())})");
        if (query.ExcludeKinds is { Count: > 0 }) q.Where.Add($"NOT (e.\"Kind\" = ANY({q.Param(query.ExcludeKinds.ToArray())}))");
        if (query.DocType is not null) q.Where.Add($"e.\"DocType\" = {q.Param(query.DocType)}");
        if (query.NotDocType is not null) q.Where.Add($"e.\"DocType\" <> {q.Param(query.NotDocType)}");
        if (!query.IncludeRemoved) q.Where.Add("NOT e.\"Removed\"");
        var limit = query.Limit is { } l ? $"LIMIT {Math.Max(l, 0)}" : "";
        return RunIndexAsync($"SELECT {IndexColumns(query.Body)} FROM {IndexTable} e {q.WhereClause} ORDER BY {DefaultOrder} {limit}", q, cancellationToken);
    }

    public async IAsyncEnumerable<ReverseIndexEntry> StreamSearchCandidatesAsync(ReverseSearchCandidates c,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var q = new Sql();
        if (c.Modules is { Count: > 0 }) q.Where.Add($"e.\"ModuleKey\" = ANY({q.Param(c.Modules.ToArray())})");
        if (c.Kinds is { Count: > 0 }) q.Where.Add($"e.\"Kind\" = ANY({q.Param(c.Kinds.ToArray())})");
        if (c.DocType is not null) q.Where.Add($"e.\"DocType\" = {q.Param(c.DocType)}");
        if (!c.IncludeRemoved) q.Where.Add("NOT e.\"Removed\"");

        // seleção (OU): algum termo/sinônimo no texto, ID exato ou nome do glossário no título/tags
        var matches = new List<string>();
        // cada condição é calculada UMA vez por linha (subconsulta) e a pontuação aproximada só combina os resultados: pesos do ranking em
        // memória (título 6, tags 4, tabelas 5, texto ~1,4 por termo), cobertura (termos que casam / termos)^1,5, peso do tipo, ID
        // exato (+100) e nome do glossário (+30). Com 3+ termos o ranking descarta cobertura < 0,34 — o banco já descarta.
        var inner = new List<string>();
        var score = new List<string>();
        var hit = new List<string>();
        var n = 0;
        foreach (var group in c.TermGroups)
        {
            string Any(string column) => "(" + string.Join(" OR ", group.Select(name => $"{column} LIKE {q.Param(LikeContains(name))}")) + ")";
            var i = n++;
            matches.Add(Any("e.\"SearchText\""));
            inner.Add($"{Any("e.\"SearchText\"")} AS x{i}, {Any("e.\"SearchTitle\"")} AS t{i}, {Any("e.\"SearchTags\"")} AS g{i}, {Any("e.\"SearchTables\"")} AS a{i}");
            score.Add($"(CASE WHEN e.t{i} THEN 6 ELSE 0 END + CASE WHEN e.g{i} THEN 4 ELSE 0 END + CASE WHEN e.a{i} THEN 5 ELSE 0 END + CASE WHEN e.x{i} THEN 1.4 ELSE 0 END)");
            hit.Add($"(CASE WHEN e.x{i} THEN 1 ELSE 0 END)");
        }
        var special = new List<string>();   // ID exato / nome do glossário: entram mesmo sem cobertura
        var bonus = new List<string>();
        var k = 0;
        foreach (var phrase in c.Phrases.Distinct())
        {
            var like = $"e.\"HeadWords\" LIKE {q.Param(LikeContains(phrase))}";
            matches.Add(like);
            inner.Add($"({like}) AS p{k}");
            special.Add($"e.p{k}");
            bonus.Add($"(CASE WHEN e.p{k} THEN 30 ELSE 0 END)");
            k++;
        }
        var ids = 0;
        foreach (var (module, id) in c.ExactIds.Distinct())
        {
            var exact = module is null ? $"e.\"ItemId\" = {q.Param(id)}" : $"(e.\"ItemId\" = {q.Param(id)} AND e.\"ModuleKey\" = {q.Param(module)})";
            matches.Add(exact);
            inner.Add($"({exact}) AS i{ids}");
            special.Add($"e.i{ids}");
            bonus.Add($"(CASE WHEN e.i{ids} THEN 100 ELSE 0 END)");
            ids++;
        }
        string sql;
        if (matches.Count > 0)
        {
            q.Where.Add(string.Join(" OR ", matches));
            var hits = string.Join(" + ", hit.DefaultIfEmpty("0"));
            var total = Math.Max(c.TermGroups.Count, 1);
            var coverage = total >= 3 ? $"(({hits})::float8 / {total} >= 0.34{(special.Count > 0 ? " OR " + string.Join(" OR ", special) : "")})" : "TRUE";
            var kind = "(CASE WHEN e.\"Kind\" IN ('RN','UC') THEN 1.25 WHEN e.\"Kind\" IN ('API','TELA','INT','DB') THEN 1.1 WHEN e.\"Kind\" = 'GAP' THEN 0.8 ELSE 1 END)";
            var rank = $"(({string.Join(" + ", score.Concat(bonus).DefaultIfEmpty("0"))}) * power(GREATEST(({hits})::float8 / {total}, 0.05), 1.5) * {kind})";
            var extra = inner.Count > 0 ? ", " + string.Join(", ", inner) : "";
            // o ranking é calculado só com as colunas pequenas; o texto (Body, que pode estar comprimido no TOAST) vem só das linhas escolhidas
            sql = $"WITH f AS (SELECT e.\"Id\", e.\"ModuleKey\", e.\"ItemId\", e.\"Kind\"{extra} FROM {IndexTable} e {q.WhereClause}), " +
                  $"r AS (SELECT e.\"Id\", e.\"ModuleKey\", e.\"ItemId\", {rank} AS rk FROM f e WHERE {coverage} ORDER BY rk DESC, e.\"ModuleKey\", e.\"ItemId\" LIMIT {Math.Max(c.Max, 1)}) " +
                  $"SELECT {IndexColumns(ReverseBodyMode.Full)} FROM r JOIN {IndexTable} e ON e.\"Id\" = r.\"Id\" ORDER BY r.rk DESC, e.\"ModuleKey\", e.\"ItemId\"";
        }
        else   // sem termo nem ID: a ordem por referência da busca em memória
            sql = $"SELECT {IndexColumns(ReverseBodyMode.Full)} FROM {IndexTable} e {q.WhereClause} ORDER BY e.\"ModuleKey\" || '#' || e.\"ItemId\" COLLATE \"C\" LIMIT {Math.Max(c.Max, 1)}";
        await foreach (var row in context.Database.SqlQueryRaw<IndexRow>(sql, q.Args).AsAsyncEnumerable().WithCancellation(cancellationToken))
            yield return row.ToEntry();
    }

    public async Task<List<ReverseIndexEntry>> GetIndexItemsByRefsAsync(IReadOnlyCollection<string> refs, ReverseBodyMode body, CancellationToken cancellationToken)
    {
        var pairs = refs.Select(r => r.Split('#', 2)).Where(p => p.Length == 2).Distinct(new PairComparer()).ToList();
        if (pairs.Count == 0) return [];
        var q = new Sql();
        var mods = q.Param(pairs.Select(p => p[0]).ToArray());
        var ids = q.Param(pairs.Select(p => p[1]).ToArray());
        return await RunIndexAsync($"SELECT {IndexColumns(body)} FROM {IndexTable} e " +
                                   $"JOIN unnest({mods}::text[], {ids}::text[]) AS r(m, i) ON e.\"ModuleKey\" = r.m AND e.\"ItemId\" = r.i ORDER BY {DefaultOrder}",
            q, cancellationToken);
    }

    public Task<List<ReverseIndexEntry>> FindIndexItemsAsync(string itemId, string? moduleKey, ReverseBodyMode body, CancellationToken cancellationToken)
    {
        var q = new Sql();
        q.Where.Add($"e.\"ItemId\" = {q.Param(itemId)}");
        if (moduleKey is not null) q.Where.Add($"e.\"ModuleKey\" = {q.Param(moduleKey)}");
        return RunIndexAsync($"SELECT {IndexColumns(body)} FROM {IndexTable} e {q.WhereClause} ORDER BY {DefaultOrder}",
            q, cancellationToken);
    }

    public Task<List<ReverseIndexEntry>> GetIndexReferrersAsync(string moduleKey, string itemId, int limit, CancellationToken cancellationToken)
    {
        var q = new Sql();
        var full = q.Param($"{moduleKey}#{itemId}");
        var id = q.Param(itemId);
        var module = q.Param(moduleKey);
        q.Where.Add($"NOT (e.\"ModuleKey\" = {module} AND e.\"ItemId\" = {id})");
        q.Where.Add($"e.\"Refs\" @> jsonb_build_array({full}::text) OR (e.\"ModuleKey\" = {module} AND e.\"Refs\" @> jsonb_build_array({id}::text))");
        return RunIndexAsync($"SELECT {IndexColumns(ReverseBodyMode.None)} FROM {IndexTable} e {q.WhereClause} ORDER BY {DefaultOrder} LIMIT {Math.Max(limit, 0)}",
            q, cancellationToken);
    }

    public Task<List<ReverseIndexEntry>> GetIndexImpactAsync(string value, (string? Module, string Id)? item, int limit, CancellationToken cancellationToken)
    {
        var q = new Sql();
        q.Where.Add("NOT e.\"Removed\"");
        if (item is { } p)
        {
            var cond = $"e.\"Refs\" @> jsonb_build_array({q.Param(p.Module is null ? p.Id : $"{p.Module}#{p.Id}")}::text)";
            if (p.Module is not null)
                cond += $" OR (e.\"ModuleKey\" = {q.Param(p.Module)} AND e.\"Refs\" @> jsonb_build_array({q.Param(p.Id)}::text))";
            q.Where.Add(cond);
        }
        else
        {
            var lower = value.ToLowerInvariant();
            q.Where.Add($"e.\"Tables\" @> jsonb_build_array({q.Param(value.ToUpperInvariant())}::text)"
                        + $" OR e.\"Modules\" @> jsonb_build_array({q.Param(lower)}::text)"
                        + $" OR e.\"Refs\"::text LIKE {q.Param(LikeContains(JsonStart(lower + "#")))}"
                        + $" OR EXISTS (SELECT 1 FROM jsonb_array_elements_text(e.\"Tags\") t WHERE lower(t) = {q.Param(lower)})");
        }
        return RunIndexAsync($"SELECT {IndexColumns(ReverseBodyMode.Preview)} FROM {IndexTable} e {q.WhereClause} ORDER BY e.\"ModuleKey\", e.\"Kind\", e.\"ItemId\" LIMIT {Math.Max(limit, 0)}",
            q, cancellationToken);
    }

    public Task<List<ReverseIndexEntry>> GetIndexCitingModuleAsync(string moduleKey, int limit, CancellationToken cancellationToken)
    {
        var q = new Sql();
        q.Where.Add("NOT e.\"Removed\"");
        q.Where.Add($"e.\"ModuleKey\" <> {q.Param(moduleKey)}");
        q.Where.Add($"e.\"Modules\" @> jsonb_build_array({q.Param(moduleKey)}::text) OR e.\"Refs\"::text LIKE {q.Param(LikeContains(JsonStart(moduleKey + "#")))}");
        return RunIndexAsync($"SELECT {IndexColumns(ReverseBodyMode.None)} FROM {IndexTable} e {q.WhereClause} ORDER BY {DefaultOrder} LIMIT {Math.Max(limit, 0)}",
            q, cancellationToken);
    }

    public async Task<HashSet<string>> GetIndexModuleKeysAsync(CancellationToken cancellationToken) =>
        (await context.ReverseIndexEntries.AsNoTracking().Where(e => !e.Removed).Select(e => e.ModuleKey).Distinct().ToListAsync(cancellationToken)).ToHashSet();

    /// <summary>
    /// O começo de um valor dentro da lista, como aparece no <c>jsonb::text</c> (<c>"valor#</c>, com a aspa de abertura e sem a de
    /// fechamento) — para achar "algum elemento começa com X" com <c>LIKE</c>.
    /// </summary>
    private static string JsonStart(string value) => JsonSerializer.Serialize(value, JsonOptions)[..^1];

    /// <summary>Padrão <c>LIKE %texto%</c> com o texto escapado (<c>\</c>, <c>%</c>, <c>_</c>).</summary>
    private static string LikeContains(string text)
    {
        var sb = new StringBuilder("%");
        foreach (var ch in text)
        {
            if (ch is '\\' or '%' or '_') sb.Append('\\');
            sb.Append(ch);
        }
        return sb.Append('%').ToString();
    }

    private sealed class PairComparer : IEqualityComparer<string[]>
    {
        public bool Equals(string[]? x, string[]? y) => x is not null && y is not null && x[0] == y[0] && x[1] == y[1];
        public int GetHashCode(string[] p) => HashCode.Combine(p[0], p[1]);
    }
}
