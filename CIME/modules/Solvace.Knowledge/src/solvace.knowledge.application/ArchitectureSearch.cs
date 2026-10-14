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

    /// <summary>
    /// Texto normalizado de cada seção pela versão (0070: uma entrada por seção — a versão nova substitui a velha; antes
    /// cada versão ficava para sempre). Os cabeçalhos guardam o título original para o trecho.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, (int Version, PreparedSection Prepared)> Cache = new();

    /// <summary>Seção preparada para a busca: texto sem acento/caixa e os cabeçalhos (posição, normalizado, original).</summary>
    public sealed record PreparedSection(string Text, List<(int Pos, string Title, string Original)> Headings);

    /// <summary>Resultado antes do trecho: a seção (ou o artigo) e a posição do primeiro termo no texto normalizado.</summary>
    public sealed record Candidate(ArchitectureSearchHit Hit, ArchitectureSection? Section, PreparedSection? Prepared, KnowledgeArticle? Article, string? ArticleText, int Position);

    /// <summary>A seção já preparada (mesma versão) — sem ler o conteúdo.</summary>
    public static PreparedSection? Cached(ArchitectureSection section) =>
        Cache.TryGetValue(section.Id, out var c) && c.Version == section.Version ? c.Prepared : null;

    /// <summary>Prepara e guarda a seção (o conteúdo não fica no cache, só o texto normalizado).</summary>
    public static PreparedSection Remember(ArchitectureSection section, string content)
    {
        var prepared = Prepare(content);
        Cache[section.Id] = (section.Version, prepared);
        return prepared;
    }

    /// <summary>
    /// Texto e título normalizados dos artigos do Knowledge Center pelo hash do conteúdo — antes cada busca normalizava
    /// todos os artigos de novo (o <c>Normalize(FormD)</c> caractere a caractere é a parte cara da busca).
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, (string Hash, string Text, string Title)> Articles = new();

    private static (string Text, string Title) PreparedArticle(KnowledgeArticle article)
    {
        var titleSource = $"{article.Title} {string.Join(' ', article.Tags)} {article.Category} {article.Subcategory}";
        var hash = $"{article.ContentHash}|{titleSource.GetHashCode()}";
        if (article.Id != Guid.Empty && Articles.TryGetValue(article.Id, out var cached) && cached.Hash == hash) return (cached.Text, cached.Title);
        var prepared = (Text: Normalize(article.Content), Title: Normalize(titleSource));
        if (article.Id != Guid.Empty)
        {
            if (Articles.Count > 20_000) Articles.Clear();
            Articles[article.Id] = (hash, prepared.Text, prepared.Title);
        }
        return prepared;
    }

    /// <summary>
    /// Termos com peso para escolher blocos pelo assunto de uma conversa: IDs de itens citados (UI-1052, RN-012) pesam
    /// mais, depois nomes técnicos (svc-filters-sidebar, isTotallyEmpty, clearFiltersField), depois as palavras; as
    /// mensagens anteriores (<paramref name="earlier"/>) pesam a metade.
    /// </summary>
    public static List<(string Term, double Weight)> WeightedTerms(string last, string? earlier = null)
    {
        var terms = new Dictionary<string, double>(StringComparer.Ordinal);
        void Add(string term, double weight)
        {
            if (term.Length < 2) return;
            terms[term] = Math.Max(terms.GetValueOrDefault(term), weight);
        }
        foreach (var (text, factor) in new[] { (earlier ?? string.Empty, 0.5), (last, 1.0) })
        {
            foreach (var w in Terms(text, max: 80)) Add(w, 1 * factor);
            foreach (var t in TechnicalToken().Matches(text).Select(m => m.Value).Distinct())
                if (Terms(null, [t]).FirstOrDefault() is { } n) Add(n, 2.5 * factor);
            foreach (var id in ItemId().Matches(text).Select(m => Normalize(m.Value)).Distinct()) Add(id, 6 * factor);
        }
        return terms.Select(t => (t.Key, t.Value)).ToList();
    }

    /// <summary>
    /// Ordena blocos de um documento pelo assunto (chat de melhoria em seção grande) — BM25: cada termo vale pela
    /// raridade no documento ("svc"/"component" aparecem em quase todo bloco; "cleanable" em poucos), a repetição satura
    /// e o tamanho do bloco é compensado (bloco longo não ganha só por ter mais palavras); termo no cabeçalho vale o dobro.
    /// Devolve os índices com pontuação &gt; 0, do melhor para o pior.
    /// </summary>
    public static List<int> RankBlocks(IReadOnlyList<(string Text, string? Heading)> blocks, IReadOnlyList<(string Term, double Weight)> terms)
    {
        const double k1 = 1.2, b = 0.75;
        var texts = blocks.Select(x => (Text: Normalize(x.Text), Heading: Normalize(x.Heading))).ToList();
        var avg = Math.Max(1, texts.Average(x => (double)x.Text.Length));
        var scores = new double[blocks.Count];
        foreach (var (term, weight) in terms)
        {
            var counts = texts.Select(x => CountTerm(x.Text, term, out _)).ToArray();
            var df = counts.Count(c => c > 0);
            if (df == 0) continue;
            var idf = Math.Log(1 + (blocks.Count - df + 0.5) / (df + 0.5));
            for (var i = 0; i < blocks.Count; i++)
            {
                var tf = counts[i];
                if (tf == 0) continue;
                var norm = tf * (k1 + 1) / (tf + k1 * (1 - b + b * texts[i].Text.Length / avg));
                scores[i] += weight * idf * norm * (HasTerm(texts[i].Heading, term) ? 2 : 1);
            }
        }
        return Enumerable.Range(0, blocks.Count).Where(i => scores[i] > 0).OrderByDescending(i => scores[i]).ThenBy(i => i).ToList();
    }

    /// <summary>
    /// Posição (no texto normalizado) onde os termos aparecem mais juntos: a janela de <paramref name="window"/>/2
    /// caracteres com mais termos distintos (desempate: mais ocorrências). -1 = nenhum termo no texto.
    /// </summary>
    public static int DensestWindow(string text, IReadOnlyList<string> terms, int window)
    {
        var hits = new List<(int Pos, int Term)>();
        for (var t = 0; t < terms.Count; t++)
        {
            var term = terms[t];
            if (string.IsNullOrEmpty(term)) continue;
            var count = 0;
            for (var i = text.IndexOf(term, StringComparison.Ordinal); i >= 0 && count < 200; i = text.IndexOf(term, i + term.Length, StringComparison.Ordinal))
            {
                if (term.Length <= 2 && !Bounded(text, i, term.Length)) continue;
                hits.Add((i, t));
                count++;
            }
        }
        if (hits.Count == 0) return -1;
        hits.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        var span = Math.Max(1, window / 2);
        var inWindow = new Dictionary<int, int>();
        int left = 0, best = hits[0].Pos, bestDistinct = 0, bestTotal = 0;
        for (var right = 0; right < hits.Count; right++)
        {
            inWindow[hits[right].Term] = inWindow.GetValueOrDefault(hits[right].Term) + 1;
            while (hits[right].Pos - hits[left].Pos > span)
            {
                var term = hits[left].Term;
                if (--inWindow[term] == 0) inWindow.Remove(term);
                left++;
            }
            var total = right - left + 1;
            if (inWindow.Count > bestDistinct || (inWindow.Count == bestDistinct && total > bestTotal))
            {
                bestDistinct = inWindow.Count;
                bestTotal = total;
                best = hits[left].Pos;
            }
        }
        return best;
    }

    /// <summary>Esquece as seções que não existem mais (removidas, substituídas ou fora da busca).</summary>
    public static void Forget(IReadOnlySet<Guid> keep)
    {
        foreach (var id in Cache.Keys)
            if (!keep.Contains(id)) Cache.TryRemove(id, out _);
    }

    /// <summary>
    /// 0070: texto longo normalizado em pedaços (cortados em quebra de linha): o <c>Normalize(FormD)</c> aluga do
    /// <c>ArrayPool</c> um buffer do tamanho do texto inteiro, e o pool guardava um por thread (documentos de milhões de caracteres).
    /// </summary>
    private const int NormalizeChunk = 16_384;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        for (var start = 0; start < value.Length;)
        {
            var end = Math.Min(value.Length, start + NormalizeChunk);
            if (end < value.Length)
            {
                var newline = value.LastIndexOf('\n', end - 1, end - start);
                if (newline > start) end = newline + 1;
                else if (char.IsHighSurrogate(value[end - 1])) end--;
            }
            var piece = start == 0 && end == value.Length ? value : value[start..end];
            foreach (var c in piece.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToLowerInvariant(c));
            start = end;
        }
        return sb.ToString();
    }

    /// <summary>Termos da busca: palavras significativas (radical) e, se vierem, frases/nomes técnicos extras (inteiros).</summary>
    public static List<string> Terms(string? query, IEnumerable<string>? extra = null, int max = 24)
    {
        var terms = new List<string>();
        foreach (var raw in WordPattern().Matches(Normalize(query)).Select(m => m.Value))
        {
            // 0056: sigla curta com dígito ("a3", "5s") vale — antes "A3" sumia da busca, do MCP e do Pergunte.
            if (StopWords.Contains(raw) || (raw.Length < 3 && !raw.Any(char.IsDigit))) continue;
            terms.Add(Stem(raw));
        }
        foreach (var e in extra ?? [])
        {
            var n = Normalize(e).Trim();
            if (n.Length < 3 && !(n.Length == 2 && n.Any(char.IsDigit))) continue;
            terms.Add(n.Contains(' ') || n.Contains('_') ? n : Stem(n));
        }
        return terms.Distinct().Take(max).ToList();
    }

    /// <summary>Radical de uma palavra já normalizada (o mesmo da busca) — para os sinônimos (0053).</summary>
    public static string StemWord(string word) => Stem(word);

    /// <summary>Radical simples para palavras; nomes técnicos (com _ ou dígito, ex.: TB_WCM_USER) ficam inteiros.</summary>
    private static string Stem(string word) =>
        word.Length > 5 && !word.Contains('_') && !word.Any(char.IsDigit) ? word[..(word.Length - 2)] : word;

    public static List<ArchitectureSearchHit> Run(IReadOnlyList<ArchitectureProject> projects, IReadOnlyList<KnowledgeArticle> articles,
        IReadOnlyList<string> terms, int limit, IReadOnlyCollection<string>? boostProjects = null, IReadOnlyCollection<string>? boostSections = null,
        ReverseSynonyms? synonyms = null)
    {
        var candidates = Score(projects, articles, terms, limit, boostProjects, boostSections, synonyms,
            s => Cached(s) ?? Remember(s, s.Content));
        foreach (var c in candidates)
        {
            if (c.Section is not null)
            {
                var (snippet, heading) = Snippet(c.Section.Content, c.Position);
                c.Hit.Snippet = snippet;
                c.Hit.Heading = HeadingAt(c.Prepared!, c.Position) ?? heading;
            }
            else c.Hit.Snippet = Snippet(c.Article!.Content, c.Position).Snippet;
        }
        return candidates.Select(c => c.Hit).ToList();
    }

    /// <summary>
    /// Pontua seções e artigos e devolve os <paramref name="limit"/> melhores SEM o trecho (0070: quem chama busca no
    /// banco só o pedaço do texto em volta de <see cref="Candidate.Position"/>). <paramref name="prepare"/> devolve a
    /// seção preparada (do cache ou lendo o conteúdo).
    /// </summary>
    public static List<Candidate> Score(IReadOnlyList<ArchitectureProject> projects, IReadOnlyList<KnowledgeArticle> articles,
        IReadOnlyList<string> terms, int limit, IReadOnlyCollection<string>? boostProjects, IReadOnlyCollection<string>? boostSections,
        ReverseSynonyms? synonyms, Func<ArchitectureSection, PreparedSection> prepare, CancellationToken cancellationToken = default)
    {
        synonyms ??= ReverseSynonyms.Empty;
        if (terms.Count == 0) return [];
        var hits = new List<Candidate>();
        var minCoverage = terms.Count >= 3 ? 0.34 : 0.0;

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested(); // a tela desistiu (nova busca): não gasta a CPU da instância à toa
            var projectText = Normalize($"{project.Name} {project.Key} {project.Summary} {string.Join(' ', project.Keywords)}");
            var boost = boostProjects?.Contains(project.Key) == true ? 1.6 : 1.0;
            foreach (var section in project.Sections)
            {
                // 0040: pergunta de operação puxa as seções de configuração/operação.
                var sectionBoost = boostSections?.Contains(section.Key) == true ? 2.5 : 1.0;
                var prepared = prepare(section);
                var (text, headings) = (prepared.Text, prepared.Headings);
                var title = Normalize(section.Title);
                double score = 0;
                var matched = new List<string>();
                var firstPos = -1;
                foreach (var term in terms)
                {
                    // 0053: o termo casa também pelos sinônimos do glossário do projeto ("RCA" ↔ "A3").
                    var alts = synonyms.Alternatives(project.Key, term);
                    var inTitle = HasTerm(title, term) || alts.Any(a => HasTerm(title, a));
                    var inProject = HasTerm(projectText, term) || alts.Any(a => HasTerm(projectText, a));
                    var count = Count(text, term, out var pos);
                    foreach (var alt in alts)
                    {
                        if (count > 0) break;
                        count = Count(text, alt, out pos);
                    }
                    var inHeading = headings.Any(h => HasTerm(h.Title, term) || alts.Any(a => HasTerm(h.Title, a)));
                    if (!inTitle && !inProject && count == 0) continue;
                    matched.Add(term);
                    score += (inTitle ? 6 : 0) + (inProject ? 3 : 0) + (inHeading ? 4 : 0) + (count > 0 ? 1 + Math.Min(count, 6) * 0.4 : 0);
                    if (pos >= 0 && (firstPos < 0 || (count > 0 && count < 4))) firstPos = pos;
                }
                var coverage = (double)matched.Count / terms.Count;
                if (matched.Count == 0 || coverage < minCoverage) continue;
                hits.Add(new Candidate(new ArchitectureSearchHit
                {
                    Type = "section", ProjectKey = project.Key, ProjectName = project.Name, SectionKey = section.Key, SectionTitle = section.Title, Audience = section.Audience,
                    Title = $"{project.Name} — {section.Title}",
                    Score = Math.Round(score * Math.Pow(coverage, 1.5) * boost * sectionBoost, 2), Matched = matched
                }, section, prepared, null, null, firstPos));
            }
        }

        foreach (var article in articles)
        {
            var (text, title) = PreparedArticle(article);
            double score = 0;
            var matched = new List<string>();
            var firstPos = -1;
            foreach (var term in terms)
            {
                var alts = synonyms.Alternatives("*", term);
                var inTitle = HasTerm(title, term) || alts.Any(a => HasTerm(title, a));
                var count = Count(text, term, out var pos);
                foreach (var alt in alts)
                {
                    if (count > 0) break;
                    count = Count(text, alt, out pos);
                }
                if (!inTitle && count == 0) continue;
                matched.Add(term);
                score += (inTitle ? 6 : 0) + (count > 0 ? 1 + Math.Min(count, 6) * 0.4 : 0);
                if (pos >= 0 && firstPos < 0) firstPos = pos;
            }
            var coverage = (double)matched.Count / terms.Count;
            if (matched.Count == 0 || coverage < minCoverage) continue;
            hits.Add(new Candidate(new ArchitectureSearchHit
            {
                Type = "article", ArticleNumber = article.ArticleNumber, Title = $"ART-{article.ArticleNumber} — {article.Title}",
                Score = Math.Round(score * Math.Pow(coverage, 1.5), 2), Matched = matched
            }, null, null, article, text, firstPos));
        }

        return hits.OrderByDescending(h => h.Hit.Score).ThenBy(h => h.Hit.Title).Take(limit).ToList();
    }

    /// <summary>Trecho de um artigo do KC (o texto do artigo é pequeno e vem inteiro).</summary>
    public static string ArticleSnippet(KnowledgeArticle article, int position) => Snippet(article.Content, position).Snippet;

    /// <summary>
    /// Janela do texto original em volta da posição, em code points (o <c>substring</c> do PostgreSQL): a normalização
    /// mantém o tamanho em UTF-16, então a janela é medida no texto normalizado e convertida. <c>Cut</c>/<c>More</c> =
    /// há texto antes/depois (as reticências).
    /// </summary>
    public static (int Start, int Length, bool Cut, bool More) SnippetWindow(PreparedSection prepared, int position)
    {
        var text = prepared.Text;
        var at = Math.Clamp(position < 0 ? 0 : position, 0, Math.Max(0, text.Length - 1));
        var start = Math.Max(0, at - SnippetChars / 3);
        var end = Math.Min(text.Length, start + SnippetChars);
        return (Contracts.CodePoints.Count(text, 0, start), Contracts.CodePoints.Count(text, start, end), start > 0, end < text.Length);
    }

    /// <summary>Trecho a partir da janela lida do banco (ver <see cref="SnippetWindow"/>).</summary>
    public static string SnippetFromWindow(string window, bool cut, bool more)
    {
        var clean = MarkdownNoise().Replace(window, " ");
        clean = Spaces().Replace(clean, " ").Trim();
        if (cut) clean = "…" + clean;
        if (more) clean += "…";
        return clean;
    }

    /// <summary>O cabeçalho (como está no texto) anterior à posição.</summary>
    public static string? HeadingAt(PreparedSection prepared, int position)
    {
        var at = Math.Max(0, position);
        var match = prepared.Headings.LastOrDefault(h => h.Pos <= at);
        return match.Original is null ? null : match.Original.Trim().Trim('*', '`');
    }

    private static PreparedSection Prepare(string content)
    {
        var text = Normalize(content);
        var headings = HeadingPattern().Matches(content)
            .Select(m => (m.Index, Normalize(m.Groups[1].Value.Trim()), m.Groups[1].Value))
            .ToList();
        return new PreparedSection(text, headings);
    }

    private static int Count(string text, string term, out int first) => CountTerm(text, term, out first);

    /// <summary>
    /// Ocorrências do termo no texto (até 50). 0056: termo curto (até 2 caracteres, ex.: "a3") só casa como palavra
    /// inteira — não dentro de "sa3_registro" nem de um GUID ("9a3c").
    /// </summary>
    public static int CountTerm(string text, string term, out int first)
    {
        first = -1;
        if (string.IsNullOrEmpty(term)) return 0;
        var count = 0;
        for (var i = text.IndexOf(term, StringComparison.Ordinal); i >= 0 && count < 50; i = text.IndexOf(term, i + term.Length, StringComparison.Ordinal))
        {
            if (term.Length <= 2 && !Bounded(text, i, term.Length)) continue;
            if (first < 0) first = i;
            count++;
        }
        return count;
    }

    /// <summary>O texto contém o termo (curto: como palavra inteira — ver <see cref="CountTerm"/>).</summary>
    public static bool HasTerm(string text, string term) =>
        term.Length > 2 ? text.Contains(term, StringComparison.Ordinal) : CountTerm(text, term, out _) > 0;

    private static bool Bounded(string text, int start, int length) =>
        (start == 0 || !char.IsLetterOrDigit(text[start - 1])) && (start + length >= text.Length || !char.IsLetterOrDigit(text[start + length]));

    /// <summary>Trecho em volta da posição (sem marcação de markdown) e o cabeçalho anterior a ela, como está no texto.</summary>
    private static (string Snippet, string? Heading) Snippet(string original, int pos)
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

    /// <summary>Nome técnico: com hífen/ponto/sublinhado entre partes (svc-filters-sidebar) ou camelCase (isTotallyEmpty).</summary>
    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9]*(?:[-_.][A-Za-z0-9]+)+\b|\b[a-z]+[A-Z][A-Za-z0-9]*\b")]
    private static partial Regex TechnicalToken();

    /// <summary>ID de item da engenharia reversa (UI-1052, RN-012, GAP-993).</summary>
    [GeneratedRegex(@"\b[A-Z]{2,5}-\d{1,5}\b")]
    private static partial Regex ItemId();

    [GeneratedRegex(@"^#{1,4}\s+(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"[#*`>|]+|\[(?=[^\]]*\]\()|\]\([^)]*\)|```[a-z]*")]
    private static partial Regex MarkdownNoise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
