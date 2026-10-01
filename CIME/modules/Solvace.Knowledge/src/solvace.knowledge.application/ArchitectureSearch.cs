using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Responses;

namespace solvace.knowledge.application;

/// <summary>
/// Busca no conteúdo da Base Solvace (0037): seções de todos os projetos e artigos do Knowledge Center. Sem acento,
/// sem caixa, sem palavras vazias e com um radical simples (usuário/usuários, sucesso/sucessos); título e cabeçalhos
/// pesam mais que o texto e quem cobre mais termos da busca sobe. Devolve o trecho e o cabeçalho mais próximo.
/// O texto normalizado de cada seção fica em cache pela versão (a base muda pouco).
/// </summary>
public static partial class ArchitectureSearch
{
    private const int SnippetChars = 260;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "o", "as", "os", "um", "uma", "uns", "umas", "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas", "por", "pelo",
        "pela", "para", "pra", "com", "sem", "que", "se", "e", "ou", "como", "qual", "quais", "quando", "onde", "porque", "quem", "isso",
        "esse", "essa", "este", "esta", "ele", "ela", "eles", "elas", "ao", "aos", "seu", "sua", "seus", "suas", "eu", "voce", "saber",
        "fazer", "faz", "fez", "tem", "ter", "ha", "sao", "ser", "foi", "esta", "estao", "the", "of", "to", "in", "and", "is", "how", "what",
        "does", "do", "for", "on", "with", "mais", "muito", "entre", "sobre", "ja", "nao", "sim", "existe", "algum", "alguma", "consigo",
        "posso", "pode", "deve", "precisa", "preciso", "vez", "tipo", "coisa"
    };

    private static readonly ConcurrentDictionary<(Guid, int), (string Text, List<(int Pos, string Title)> Headings)> Cache = new();

    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>Termos da busca: palavras significativas (radical) e, se vierem, frases/nomes técnicos extras (inteiros).</summary>
    public static List<string> Terms(string? query, IEnumerable<string>? extra = null)
    {
        var terms = new List<string>();
        foreach (var raw in WordPattern().Matches(Normalize(query)).Select(m => m.Value))
        {
            if (StopWords.Contains(raw) || (raw.Length < 3 && !raw.All(char.IsDigit))) continue;
            terms.Add(Stem(raw));
        }
        foreach (var e in extra ?? [])
        {
            var n = Normalize(e).Trim();
            if (n.Length < 3) continue;
            terms.Add(n.Contains(' ') || n.Contains('_') ? n : Stem(n));
        }
        return terms.Distinct().Take(24).ToList();
    }

    /// <summary>Radical simples para palavras; nomes técnicos (com _ ou dígito, ex.: TB_WCM_USER) ficam inteiros.</summary>
    private static string Stem(string word) =>
        word.Length > 5 && !word.Contains('_') && !word.Any(char.IsDigit) ? word[..(word.Length - 2)] : word;

    public static List<ArchitectureSearchHit> Run(IReadOnlyList<ArchitectureProject> projects, IReadOnlyList<KnowledgeArticle> articles,
        IReadOnlyList<string> terms, int limit, IReadOnlyCollection<string>? boostProjects = null)
    {
        if (terms.Count == 0) return [];
        var hits = new List<ArchitectureSearchHit>();
        var minCoverage = terms.Count >= 3 ? 0.34 : 0.0;

        foreach (var project in projects)
        {
            var projectText = Normalize($"{project.Name} {project.Key} {project.Summary} {string.Join(' ', project.Keywords)}");
            var boost = boostProjects?.Contains(project.Key) == true ? 1.6 : 1.0;
            foreach (var section in project.Sections)
            {
                var (text, headings) = Cache.GetOrAdd((section.Id, section.Version), _ => Prepare(section.Content));
                var title = Normalize(section.Title);
                double score = 0;
                var matched = new List<string>();
                var firstPos = -1;
                foreach (var term in terms)
                {
                    var inTitle = title.Contains(term);
                    var inProject = projectText.Contains(term);
                    var count = Count(text, term, out var pos);
                    var inHeading = headings.Any(h => h.Title.Contains(term));
                    if (!inTitle && !inProject && count == 0) continue;
                    matched.Add(term);
                    score += (inTitle ? 6 : 0) + (inProject ? 3 : 0) + (inHeading ? 4 : 0) + (count > 0 ? 1 + Math.Min(count, 6) * 0.4 : 0);
                    if (pos >= 0 && (firstPos < 0 || (count > 0 && count < 4))) firstPos = pos;
                }
                var coverage = (double)matched.Count / terms.Count;
                if (matched.Count == 0 || coverage < minCoverage) continue;
                var (snippet, heading) = Snippet(section.Content, text, headings, firstPos);
                hits.Add(new ArchitectureSearchHit
                {
                    Type = "section", ProjectKey = project.Key, ProjectName = project.Name, SectionKey = section.Key, SectionTitle = section.Title,
                    Title = $"{project.Name} — {section.Title}", Heading = heading, Snippet = snippet,
                    Score = Math.Round(score * Math.Pow(coverage, 1.5) * boost, 2), Matched = matched
                });
            }
        }

        foreach (var article in articles)
        {
            var text = Normalize(article.Content);
            var title = Normalize($"{article.Title} {string.Join(' ', article.Tags)} {article.Category} {article.Subcategory}");
            double score = 0;
            var matched = new List<string>();
            var firstPos = -1;
            foreach (var term in terms)
            {
                var inTitle = title.Contains(term);
                var count = Count(text, term, out var pos);
                if (!inTitle && count == 0) continue;
                matched.Add(term);
                score += (inTitle ? 6 : 0) + (count > 0 ? 1 + Math.Min(count, 6) * 0.4 : 0);
                if (pos >= 0 && firstPos < 0) firstPos = pos;
            }
            var coverage = (double)matched.Count / terms.Count;
            if (matched.Count == 0 || coverage < minCoverage) continue;
            hits.Add(new ArchitectureSearchHit
            {
                Type = "article", ArticleNumber = article.ArticleNumber, Title = $"ART-{article.ArticleNumber} — {article.Title}",
                Snippet = Snippet(article.Content, text, [], firstPos).Snippet,
                Score = Math.Round(score * Math.Pow(coverage, 1.5), 2), Matched = matched
            });
        }

        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Title).Take(limit).ToList();
    }

    private static (string, List<(int, string)>) Prepare(string content)
    {
        var text = Normalize(content);
        var headings = HeadingPattern().Matches(content)
            .Select(m => (m.Index, Normalize(m.Groups[1].Value.Trim())))
            .ToList();
        return (text, headings);
    }

    private static int Count(string text, string term, out int first)
    {
        first = text.IndexOf(term, StringComparison.Ordinal);
        if (first < 0) return 0;
        var count = 0;
        for (var i = first; i >= 0 && count < 50; i = text.IndexOf(term, i + term.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    /// <summary>Trecho em volta da posição (sem marcação de markdown) e o cabeçalho anterior a ela, como está no texto.</summary>
    private static (string Snippet, string? Heading) Snippet(string original, string normalized, List<(int Pos, string Title)> headings, int pos)
    {
        // A normalização mantém o tamanho na prática (só remove acentos combinados); a posição serve para o original.
        var at = Math.Clamp(pos < 0 ? 0 : pos, 0, Math.Max(0, original.Length - 1));
        var start = Math.Max(0, at - SnippetChars / 3);
        var end = Math.Min(original.Length, start + SnippetChars);
        var raw = original[start..end];
        var clean = MarkdownNoise().Replace(raw, " ");
        clean = Spaces().Replace(clean, " ").Trim();
        if (start > 0) clean = "…" + clean;
        if (end < original.Length) clean += "…";

        string? heading = null;
        var match = HeadingPattern().Matches(original).LastOrDefault(m => m.Index <= at);
        if (match is not null) heading = match.Groups[1].Value.Trim().Trim('*', '`');
        return (clean, heading);
    }

    [GeneratedRegex(@"[\p{L}\p{N}_]+")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"^#{1,4}\s+(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"[#*`>|]+|\[(?=[^\]]*\]\()|\]\([^)]*\)|```[a-z]*")]
    private static partial Regex MarkdownNoise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
