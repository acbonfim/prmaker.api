using System.Text.RegularExpressions;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Reverse;
using solvace.knowledge.domain.Responses;

namespace solvace.knowledge.application;

/// <summary>
/// Busca nos itens publicados da engenharia reversa (0052): ID exato vale mais que tudo, depois título, tabelas, tags e
/// o texto. Mesma normalização da busca da Base Solvace (sem acento/caixa, radical simples). Os itens preparados ficam em
/// memória até o índice mudar (marca: quantidade + última atualização).
/// </summary>
public static partial class ReverseSearch
{
    private sealed record Prepared(ReverseIndexEntry Entry, string Title, string Tags, string Tables, string Body);

    private static readonly object Gate = new();
    private static (int Count, DateTimeOffset? Last) _stamp = (-1, null);
    private static List<Prepared> _prepared = [];
    private static ReverseSynonyms _synonyms = ReverseSynonyms.Empty;

    /// <summary>Sinônimos do glossário publicado (0053), do mesmo cache dos itens.</summary>
    public static async Task<ReverseSynonyms> SynonymsAsync(Contracts.IKnowledgeRepository repository, CancellationToken cancellationToken)
    {
        await EnsureAsync(repository, cancellationToken);
        lock (Gate) return _synonyms;
    }

    /// <summary>Itens preparados, recarregando do banco só quando a marca muda.</summary>
    public static async Task<IReadOnlyList<ReverseIndexEntry>> EntriesAsync(Contracts.IKnowledgeRepository repository, CancellationToken cancellationToken)
    {
        await EnsureAsync(repository, cancellationToken);
        lock (Gate) return _prepared.Select(p => p.Entry).ToList();
    }

    private static async Task<List<Prepared>> EnsureAsync(Contracts.IKnowledgeRepository repository, CancellationToken cancellationToken)
    {
        var stamp = await repository.GetIndexStampAsync(cancellationToken);
        lock (Gate)
            if (stamp == _stamp) return _prepared;
        var entries = await repository.GetIndexEntriesAsync(null, cancellationToken);
        var prepared = entries.Select(e => new Prepared(e,
            ArchitectureSearch.Normalize($"{e.ItemId} {e.Title}"),
            ArchitectureSearch.Normalize(string.Join(' ', e.Tags) + " " + string.Join(' ', e.Synonyms) + " " + string.Join(' ', e.Modules)),
            ArchitectureSearch.Normalize(string.Join(' ', e.Tables)),
            ArchitectureSearch.Normalize(e.Body))).ToList();
        var synonyms = new ReverseSynonyms(entries);
        lock (Gate)
        {
            _stamp = stamp;
            _prepared = prepared;
            _synonyms = synonyms;
            return _prepared;
        }
    }

    /// <summary>Esquece o cache (depois de publicar, na mesma instância).</summary>
    public static void Invalidate()
    {
        lock (Gate) _stamp = (-1, null);
    }

    public static async Task<List<ReverseIndexHit>> RunAsync(Contracts.IKnowledgeRepository repository, string query,
        IReadOnlyCollection<string>? modules, IReadOnlyCollection<string>? kinds, string? docType, int limit, bool includeRemoved,
        IReadOnlyDictionary<string, string> moduleNames, CancellationToken cancellationToken)
    {
        var prepared = await EnsureAsync(repository, cancellationToken);
        ReverseSynonyms synonyms;
        lock (Gate) synonyms = _synonyms;
        var exactIds = IdPattern().Matches(query ?? string.Empty)
            .Select(m => ReverseItemKinds.ParseRef(m.Value)).Where(r => r is not null).Select(r => r!.Value).ToList();
        var terms = ArchitectureSearch.Terms(IdPattern().Replace(query ?? string.Empty, " "));
        var hits = new List<ReverseIndexHit>();
        foreach (var p in prepared)
        {
            var e = p.Entry;
            if (modules is { Count: > 0 } && !modules.Contains(e.ModuleKey)) continue;
            if (kinds is { Count: > 0 } && !kinds.Contains(e.Kind)) continue;
            if (docType is not null && e.DocType != docType) continue;
            if (e.Removed && !includeRemoved) continue;

            double score = 0;
            var exact = exactIds.Any(x => x.Id == e.ItemId && (x.Module is null || x.Module == e.ModuleKey));
            if (exact) score += 100;
            var matched = 0;
            foreach (var term in terms)
            {
                // 0053: sinônimos do glossário do módulo — "RCA" casa com "A3".
                var alts = synonyms.Alternatives(e.ModuleKey, term);
                bool Has(string text) => ArchitectureSearch.HasTerm(text, term) || alts.Any(a => ArchitectureSearch.HasTerm(text, a));
                var inTitle = Has(p.Title);
                var inTags = Has(p.Tags);
                var inTables = Has(p.Tables);
                var count = Count(p.Body, term);
                foreach (var alt in alts)
                {
                    if (count > 0) break;
                    count = Count(p.Body, alt);
                }
                if (!inTitle && !inTags && !inTables && count == 0) continue;
                matched++;
                score += (inTitle ? 6 : 0) + (inTags ? 4 : 0) + (inTables ? 5 : 0) + (count > 0 ? 1 + Math.Min(count, 6) * 0.4 : 0);
            }
            if (terms.Count > 0 && matched == 0 && !exact) continue;
            if (terms.Count == 0 && !exact && exactIds.Count > 0) continue;
            var coverage = terms.Count == 0 ? 1 : (double)matched / terms.Count;
            if (!exact && terms.Count >= 3 && coverage < 0.34) continue;
            // Regras e casos de uso respondem mais análises; itens de lacuna por último.
            var kindBoost = e.Kind switch { "RN" or "UC" => 1.25, "API" or "TELA" or "INT" or "DB" => 1.1, "GAP" => 0.8, _ => 1.0 };
            hits.Add(ToHit(e, moduleNames, Math.Round(score * Math.Pow(coverage, 1.5) * kindBoost, 2), Snippet(e.Body)));
        }
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Ref, StringComparer.Ordinal).Take(Math.Clamp(limit, 1, 100)).ToList();
    }

    public static ReverseIndexHit ToHit(ReverseIndexEntry e, IReadOnlyDictionary<string, string> moduleNames, double score, string snippet) => new()
    {
        Ref = e.Ref, ModuleKey = e.ModuleKey, ModuleName = moduleNames.GetValueOrDefault(e.ModuleKey), DocType = e.DocType, ItemId = e.ItemId,
        Kind = e.Kind, KindLabel = ReverseItemKinds.ByPrefix.TryGetValue(e.Kind, out var k) ? k.Label : e.Kind, Title = e.Title,
        Snippet = snippet, Tags = e.Tags, Tables = e.Tables, Modules = e.Modules, Synonyms = e.Synonyms, Removed = e.Removed, Score = score
    };

    /// <summary>Primeiras linhas úteis do corpo (sem o cabeçalho e sem marcação).</summary>
    public static string Snippet(string body, int max = 220)
    {
        var lines = body.Split('\n').Skip(1).Select(l => MarkdownNoise().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        var text = Spaces().Replace(string.Join(" · ", lines), " ").Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    private static int Count(string text, string term) => Math.Min(30, ArchitectureSearch.CountTerm(text, term, out _));

    [GeneratedRegex(@"(?:[a-z0-9][a-z0-9._-]*#)?\b(?:TELA|PRF|EST|NTF|CFG|REL|TEC|CMP|API|EVT|JOB|INT|FLX|OBJ|PER|GLO|ADR|NFR|SEQ|GAP|SQL|TRG|TUT|FAQ|FN|UC|RN|DB|UI)-\d{1,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"[#*`>|]+|\[(?=[^\]]*\]\()|\]\([^)]*\)|```[a-z]*")]
    private static partial Regex MarkdownNoise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
