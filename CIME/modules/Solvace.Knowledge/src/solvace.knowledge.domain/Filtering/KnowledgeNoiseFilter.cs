using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Filtering;

/// <summary>Um artigo do Knowledge Center como veio do banco dele, antes do filtro.</summary>
public sealed record KnowledgeArticleCandidate(
    int ArticleNumber,
    string? Title,
    string? Category,
    string? Subcategory,
    int StatusId,
    bool IsDeleted,
    bool CategoryActive,
    bool SubcategoryActive,
    string? ContentPlainText);

/// <summary>Regras que o administrador ACRESCENTA ao piso (plugin "Knowledge Center Configurations").</summary>
public sealed class KnowledgeFilterOptions
{
    public static readonly KnowledgeFilterOptions None = new();

    /// <summary>Padrões extras (regex, sem diferenciar maiúsculas/acentos) — somados aos do piso.</summary>
    public IReadOnlyList<string> ExtraPatterns { get; init; } = [];

    /// <summary>Artigos (número ART-n) sempre fora — vale mesmo sobre a lista de liberados.</summary>
    public IReadOnlySet<int> ExcludedArticles { get; init; } = new HashSet<int>();

    /// <summary>
    /// Artigos liberados explicitamente: passam pelos padrões de teste, categoria inativa e tamanho mínimo, mas
    /// NUNCA por status/exclusão (rascunho, arquivado e removido ficam sempre fora).
    /// </summary>
    public IReadOnlySet<int> AllowedArticles { get; init; } = new HashSet<int>();

    /// <summary>Tamanho mínimo do texto; abaixo do piso não vale (só pode aumentar).</summary>
    public int MinTextLength { get; init; }

    /// <summary>Impressão digital das regras (piso + extras): mudou = a próxima sincronização precisa ser completa.</summary>
    public string Fingerprint() =>
        string.Join("|", KnowledgeNoiseFilter.FloorPatterns) + "#" + KnowledgeNoiseFilter.FloorMinTextLength + "#"
        + string.Join("|", ExtraPatterns.Select(p => p.Trim()).Order(StringComparer.Ordinal)) + "#"
        + string.Join(",", ExcludedArticles.Order()) + "#" + string.Join(",", AllowedArticles.Order()) + "#" + MinTextLength;
}

public sealed record KnowledgeFilterVerdict(bool Accepted, string? Reason)
{
    public static readonly KnowledgeFilterVerdict Ok = new(true, null);
    public static KnowledgeFilterVerdict Rejected(string reason) => new(false, reason);
}

/// <summary>
/// Piso de filtro de dados de teste do Knowledge Center (feature 0033) — requisito fixo: NÃO é configuração e não
/// existe chave para desligá-lo. O DEV do KC é quase todo teste de QA ("title-Haroldo", "teste qa", categorias
/// "Teste QA"...); nada disso pode entrar na base que as análises usam. O script da skill aplica a mesma regra
/// (<c>kc.py</c>, mesmos padrões e mesmo mínimo); o backend reaplica em toda sincronização (a palavra final é dele).
/// </summary>
public static class KnowledgeNoiseFilter
{
    /// <summary>Status "Published" do KC.</summary>
    public const int PublishedStatusId = 30;

    /// <summary>Texto útil mínimo: abaixo disso é teste ou esqueleto.</summary>
    public const int FloorMinTextLength = 200;

    /// <summary>
    /// Padrões de teste do piso, aplicados ao título, à categoria e à subcategoria já normalizados (minúsculas, sem
    /// acento, "_" vira espaço). Mudar esta lista = mudar também <c>kc.py</c> e os testes.
    /// </summary>
    public static readonly IReadOnlyList<string> FloorPatterns =
    [
        @"^title-",            // títulos gerados por teste automatizado
        @"\btest",             // test, teste, testes, testing, testexpto
        @"\bqa\b",
        @"\bprobe\b",
        @"\beditad[oa]s?\b",   // cópias "... editado" feitas em teste de edição
        @"\btmp\b",
        @"\btemp\b",
        @"\bxpto\b",
        @"\blorem\b",
        @"\bdummy\b",
        @"categoryname",
        @"^[\d\W]+$",          // só números/pontuação ("999", "123", "012345678 0123...")
    ];

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex[] Floor = FloorPatterns.Select(Compile).OfType<Regex>().ToArray();

    public static KnowledgeFilterVerdict Evaluate(KnowledgeArticleCandidate article, KnowledgeFilterOptions? options = null)
    {
        options ??= KnowledgeFilterOptions.None;

        if (article.IsDeleted) return KnowledgeFilterVerdict.Rejected("removido");
        if (article.StatusId != PublishedStatusId) return KnowledgeFilterVerdict.Rejected("não publicado");
        if (options.ExcludedArticles.Contains(article.ArticleNumber)) return KnowledgeFilterVerdict.Rejected("excluído pelo administrador");
        if (options.AllowedArticles.Contains(article.ArticleNumber)) return KnowledgeFilterVerdict.Ok;

        if (!article.CategoryActive) return KnowledgeFilterVerdict.Rejected("categoria inativa");
        if (!article.SubcategoryActive) return KnowledgeFilterVerdict.Rejected("subcategoria inativa");

        var extra = options.ExtraPatterns.Select(Compile).OfType<Regex>().ToArray();
        foreach (var (field, value) in new[] { ("título", article.Title), ("categoria", article.Category), ("subcategoria", article.Subcategory) })
        {
            var normalized = Normalize(value);
            if (normalized.Length == 0) continue;
            if (Floor.Concat(extra).FirstOrDefault(r => IsMatch(r, normalized)) is { } hit)
                return KnowledgeFilterVerdict.Rejected($"{field} parece teste ({hit})");
        }

        var min = Math.Max(FloorMinTextLength, options.MinTextLength);
        var length = Normalize(article.ContentPlainText).Length;
        return length < min
            ? KnowledgeFilterVerdict.Rejected($"texto curto ({length} < {min})")
            : KnowledgeFilterVerdict.Ok;
    }

    /// <summary>Minúsculas, sem acento nem caracteres invisíveis, "_" como espaço e espaços colapsados.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format) continue;
            sb.Append(ch == '_' ? ' ' : char.ToLowerInvariant(ch));
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static Regex? Compile(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try
        {
            return new Regex(pattern.Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null; // padrão extra inválido no plugin: ignorado (o piso continua valendo)
        }
    }

    private static bool IsMatch(Regex regex, string value)
    {
        try { return regex.IsMatch(value); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
