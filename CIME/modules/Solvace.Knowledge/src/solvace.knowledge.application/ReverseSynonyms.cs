using solvace.knowledge.domain.Entities;

namespace solvace.knowledge.application;

/// <summary>
/// Sinônimos do glossário da engenharia reversa (0053): cada item <c>GLO</c> publicado vira um grupo (o termo + a linha
/// <c>**Sinônimos:**</c>). Um termo da busca que pertence a um grupo também casa com os outros nomes do grupo — "RCA" acha
/// itens que só dizem "A3". Os grupos valem para o módulo do glossário; <c>"*"</c> junta todos (artigos do KC).
/// </summary>
public sealed partial class ReverseSynonyms
{
    public static readonly ReverseSynonyms Empty = new([]);

    /// <summary>Módulo → grupos; cada grupo = nomes normalizados (sem acento/caixa).</summary>
    private readonly Dictionary<string, List<string[]>> _groups;

    public ReverseSynonyms(IEnumerable<ReverseIndexEntry> entries)
    {
        _groups = entries.Where(e => e.Kind == "GLO" && !e.Removed)
            .Select(e => (e.ModuleKey, Names: new[] { CleanTitle(e.Title) }.Concat(e.Synonyms)
                .Select(n => ArchitectureSearch.Normalize(n).Trim()).Where(n => n.Length >= 2).Distinct().ToArray()))
            .Where(x => x.Names.Length >= 2)
            .GroupBy(x => x.ModuleKey)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Names).ToList());
    }

    public bool IsEmpty => _groups.Count == 0;

    /// <summary>Outros nomes do mesmo conceito para o termo (já normalizado/radical) no módulo (<c>"*"</c> = qualquer módulo).</summary>
    public IReadOnlyList<string> Alternatives(string? module, string term)
    {
        if (_groups.Count == 0 || string.IsNullOrEmpty(term)) return [];
        IEnumerable<string[]> groups = module is null or "*"
            ? _groups.Values.SelectMany(g => g)
            : _groups.TryGetValue(module, out var list) ? list : [];
        var result = new List<string>();
        foreach (var group in groups)
            if (group.Any(name => Matches(name, term)))
                result.AddRange(group.Where(name => !Matches(name, term)));
        return result.Distinct().ToList();
    }

    /// <summary>
    /// 0064b: nomes do glossário (2+ palavras, em qualquer idioma) que aparecem inteiros no texto da consulta — "Compliance per
    /// Checklist" no card → o grupo inteiro (Cumprimento por Checklist, Cumplimiento por Checklist...). <paramref name="words"/>
    /// = texto já reduzido a palavras com espaço nas pontas (<see cref="Words"/>).
    /// </summary>
    public IReadOnlyList<string[]> GroupsNamedIn(string? module, string words)
    {
        if (_groups.Count == 0 || words.Length < 3) return [];
        IEnumerable<string[]> groups = module is null or "*" ? _groups.Values.SelectMany(g => g) : _groups.TryGetValue(module, out var list) ? list : [];
        return groups.Where(g => g.Any(name => Words(name) is { Length: > 0 } w && w.Count(c => c == ' ') >= 3 && words.Contains(w, StringComparison.Ordinal))).ToList();
    }

    /// <summary>" palavra palavra " — só letras e dígitos, minúsculas, sem acento (para casar frase inteira).</summary>
    public static string Words(string? text) =>
        " " + string.Join(' ', WordPattern().Matches(ArchitectureSearch.Normalize(text)).Select(m => m.Value)) + " ";

    [System.Text.RegularExpressions.GeneratedRegex(@"[a-z0-9]+")]
    private static partial System.Text.RegularExpressions.Regex WordPattern();

    /// <summary>O nome casa com o termo: igual, ou o termo é uma das palavras (radical) do nome.</summary>
    private static bool Matches(string name, string term) =>
        name == term || name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w == term || ArchitectureSearch.StemWord(w) == term);

    /// <summary>"A3 (RCA 1-pager)" → "A3 (RCA 1-pager)" e também "A3": o nome antes do parêntese conta como nome.</summary>
    private static string CleanTitle(string title) => title.Trim();

    /// <summary>Todos os termos do glossário de um módulo (título + sinônimos), como escritos — para sugerir apelidos/palavras-chave.</summary>
    public static List<string> TermsOf(IEnumerable<ReverseIndexEntry> entries, string module) =>
        entries.Where(e => e.ModuleKey == module && e.Kind == "GLO" && !e.Removed)
            .SelectMany(e => new[] { e.Title }.Concat(e.Synonyms))
            .Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
