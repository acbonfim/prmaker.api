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
    private sealed record Prepared(ReverseIndexEntry Entry, string Title, string Tags, string Tables, string Body, string TitleWords, string TagWords);

    private static readonly object Gate = new();
    private static (int Count, DateTimeOffset? Last) _stamp = (-1, null);
    /// <summary>Itens preparados e sinônimos da mesma carga (0070: lidos juntos — antes um podia vir de outra carga).</summary>
    private sealed record Snapshot(List<Prepared> Items, ReverseSynonyms Synonyms);
    private static Snapshot _snapshot = new([], ReverseSynonyms.Empty);

    /// <summary>Sinônimos do glossário publicado (0053), do mesmo cache dos itens.</summary>
    public static async Task<ReverseSynonyms> SynonymsAsync(Contracts.IKnowledgeRepository repository, CancellationToken cancellationToken)
    {
        return (await EnsureAsync(repository, cancellationToken)).Synonyms;
    }

    /// <summary>Itens preparados, recarregando do banco só quando a marca muda.</summary>
    public static async Task<IReadOnlyList<ReverseIndexEntry>> EntriesAsync(Contracts.IKnowledgeRepository repository, CancellationToken cancellationToken)
    {
        return (await EnsureAsync(repository, cancellationToken)).Items.Select(p => p.Entry).ToList();
    }

    private static async Task<Snapshot> EnsureAsync(Contracts.IKnowledgeRepository repository, CancellationToken cancellationToken)
    {
        var stamp = await repository.GetIndexStampAsync(cancellationToken);
        lock (Gate)
            if (stamp == _stamp) return _snapshot;
        // 0070: uma carga por vez (quem chega depois encontra pronto) e só o começo do texto original fica no cache
        return await HeavyReads.RunAsync(async () =>
        {
            lock (Gate)
                if (stamp == _stamp) return _snapshot;
            return await LoadAsync(repository, stamp, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// O que fica do texto original de cada item no cache (0070): a primeira linha + o texto já limpo, cortado em 260 —
    /// o suficiente para <see cref="Snippet"/> dar o mesmo trecho (220) que daria com o texto inteiro.
    /// </summary>
    public static string PreviewBody(string body)
    {
        var newline = body.IndexOf('\n');
        if (newline < 0) return body;
        var lines = body[(newline + 1)..].Split('\n').Select(l => MarkdownNoise().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        var text = Spaces().Replace(string.Join(" · ", lines), " ").Trim();
        var preview = body[..newline] + "\n" + (text.Length <= 260 ? text : text[..260]);
        return preview.Length < body.Length ? preview : body;
    }

    private static async Task<Snapshot> LoadAsync(Contracts.IKnowledgeRepository repository, (int Count, DateTimeOffset? LastUpdate) stamp,
        CancellationToken cancellationToken)
    {
        var entries = await repository.GetIndexEntriesAsync(null, cancellationToken);
        var prepared = entries.Select(e => new Prepared(e.WithBody(PreviewBody(e.Body)),
            ArchitectureSearch.Normalize($"{e.ItemId} {e.Title}"),
            ArchitectureSearch.Normalize(string.Join(' ', e.Tags) + " " + string.Join(' ', e.Synonyms) + " " + string.Join(' ', e.Modules)),
            ArchitectureSearch.Normalize(string.Join(' ', e.Tables)),
            ArchitectureSearch.Normalize(e.Body),
            ReverseSynonyms.Words(e.Title),
            ReverseSynonyms.Words(string.Join(" | ", e.Tags.Concat(e.Synonyms))))).ToList();
        var synonyms = new ReverseSynonyms(entries);
        lock (Gate)
        {
            _stamp = stamp;
            _snapshot = new Snapshot(prepared, synonyms);
            return _snapshot;
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
        var snapshot = await EnsureAsync(repository, cancellationToken);
        var (prepared, synonyms) = (snapshot.Items, snapshot.Synonyms);
        var exactIds = IdPattern().Matches(query ?? string.Empty)
            .Select(m => ReverseItemKinds.ParseRef(m.Value)).Where(r => r is not null).Select(r => r!.Value).ToList();
        var terms = ArchitectureSearch.Terms(IdPattern().Replace(query ?? string.Empty, " "));
        var scored = new List<(ReverseIndexEntry Entry, double Score)>();
        // 0060b: sinônimos por (módulo, termo) calculados uma vez — antes era por item × termo, varrendo o glossário inteiro do
        // módulo a cada item (consulta longa do for-card levava 10–40 s e o contexto da analisar-bug desistia em 30 s).
        var altCache = new Dictionary<(string, string), IReadOnlyList<string>>();
        IReadOnlyList<string> Alternatives(string module, string term)
        {
            if (!altCache.TryGetValue((module, term), out var list)) altCache[(module, term)] = list = synonyms.Alternatives(module, term);
            return list;
        }
        // 0064b: nome de tela/relatório do glossário citado na consulta (qualquer idioma) — com o texto inteiro do card (~30
        // termos) o item da tela caía no corte de cobertura; agora o item que tem esse nome no título/tags passa e sobe.
        var queryWords = ReverseSynonyms.Words(query);
        var phraseCache = new Dictionary<string, string[]>();
        string[] PhraseNames(string module)
        {
            if (!phraseCache.TryGetValue(module, out var names))
                phraseCache[module] = names = synonyms.GroupsNamedIn(module, queryWords).SelectMany(g => g).Distinct().ToArray();
            return names;
        }
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
                var alts = Alternatives(e.ModuleKey, term);
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
            var names = PhraseNames(e.ModuleKey);
            var phraseInTitle = names.Length > 0 && names.Any(n => p.TitleWords.Contains(n, StringComparison.Ordinal));
            var phrase = phraseInTitle || (names.Length > 0 && names.Any(n => p.TagWords.Contains(n, StringComparison.Ordinal)));
            if (phrase) score += phraseInTitle ? 40 : 25;
            if (terms.Count > 0 && matched == 0 && !exact && !phrase) continue;
            if (terms.Count == 0 && !exact && exactIds.Count > 0) continue;
            var coverage = terms.Count == 0 ? 1 : (double)matched / terms.Count;
            if (phrase) coverage = Math.Max(coverage, 0.8);
            if (!exact && terms.Count >= 3 && coverage < 0.34) continue;
            // Regras e casos de uso respondem mais análises; itens de lacuna por último.
            var kindBoost = e.Kind switch { "RN" or "UC" => 1.25, "API" or "TELA" or "INT" or "DB" => 1.1, "GAP" => 0.8, _ => 1.0 };
            scored.Add((e, Math.Round(score * Math.Pow(coverage, 1.5) * kindBoost, 2)));
        }
        // 0070: o trecho (split + regex) só dos que voltam — antes era montado para todo item que casava (milhares numa
        // consulta ampla) e a busca levava 1–1,6 s de CPU no Cloud Run
        return scored.OrderByDescending(x => x.Score).ThenBy(x => x.Entry.Ref, StringComparer.Ordinal).Take(Math.Clamp(limit, 1, 100))
            .Select(x => ToHit(x.Entry, moduleNames, x.Score, Snippet(x.Entry.Body))).ToList();
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
