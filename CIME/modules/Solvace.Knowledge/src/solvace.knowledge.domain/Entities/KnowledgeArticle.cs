using System.Security.Cryptography;
using System.Text;

namespace solvace.knowledge.domain.Entities;

/// <summary>
/// Cópia de um artigo do Knowledge Center que passou pelo piso de filtro (0033). Só texto puro e metadados — o
/// suficiente para a análise achar a regra de negócio sem ir ao banco do KC. Única por (ambiente, artigo de origem).
/// </summary>
public class KnowledgeArticle
{
    public const int MaxTitleLength = 500;

    public Guid Id { get; private set; }
    public string Environment { get; private set; } = KnowledgeEnvironment.Dev;
    public const int MaxSourceIdLength = 64;

    /// <summary><c>article_unique_id</c> no KC (inteiro no banco atual; texto para servir a qualquer formato).</summary>
    public string SourceId { get; private set; } = string.Empty;
    /// <summary>Número ART-n.</summary>
    public int ArticleNumber { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string? Category { get; private set; }
    public string? Subcategory { get; private set; }
    public List<string> Tags { get; private set; } = [];
    public DateTimeOffset? SourceUpdatedAt { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public DateTimeOffset SyncedAt { get; private set; }
    public string SyncedBy { get; private set; } = string.Empty;

    protected KnowledgeArticle() { }

    public KnowledgeArticle(string environment, string sourceId)
    {
        var id = (sourceId ?? string.Empty).Trim();
        if (id.Length == 0 || id.Length > MaxSourceIdLength) throw new DomainException("Identificador do artigo no KC inválido.");
        Id = Guid.NewGuid();
        Environment = KnowledgeEnvironment.Normalize(environment);
        SourceId = id;
    }

    /// <summary>Atualiza com o que veio do KC; devolve true quando algo mudou.</summary>
    public bool Apply(int articleNumber, string title, string content, string? category, string? subcategory,
        IEnumerable<string>? tags, DateTimeOffset? sourceUpdatedAt, string syncedBy, DateTimeOffset now)
    {
        var cleanTitle = title.Trim();
        if (cleanTitle.Length > MaxTitleLength) cleanTitle = cleanTitle[..MaxTitleLength];
        var cleanTags = (tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        var hash = Hash($"{articleNumber}\n{cleanTitle}\n{category}\n{subcategory}\n{string.Join("|", cleanTags)}\n{content}");

        SyncedAt = now;
        SyncedBy = syncedBy;
        if (hash == ContentHash && sourceUpdatedAt == SourceUpdatedAt) return false;

        ArticleNumber = articleNumber;
        Title = cleanTitle;
        Content = content.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        Subcategory = string.IsNullOrWhiteSpace(subcategory) ? null : subcategory.Trim();
        Tags = cleanTags;
        SourceUpdatedAt = sourceUpdatedAt;
        ContentHash = hash;
        return true;
    }

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
