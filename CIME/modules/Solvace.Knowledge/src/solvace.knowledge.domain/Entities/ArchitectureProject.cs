using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Entities;

/// <summary>Tipos de "projeto" da engenharia reversa (0033): repositórios e também visões transversais.</summary>
public static class ArchitectureProjectKind
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        "ecosystem", "legacy", "frontend", "integration", "revamp", "infra", "third-party", "auth", "business-rules", "other"
    };

    public static string Normalize(string? kind)
    {
        var value = (kind ?? "other").Trim().ToLowerInvariant();
        return All.Contains(value) ? value : throw new DomainException($"Tipo de projeto inválido: '{kind}' ({string.Join(", ", All)}).");
    }
}

/// <summary>
/// Um projeto da engenharia reversa da Solvace (0033): repositório (legado, revamp, front, integrações) ou visão
/// transversal (ecossistema, infra/AWS, terceiros, login, regras de negócio). O resumo e as palavras-chave vão para
/// o índice compacto que a skill lê primeiro.
/// </summary>
public class ArchitectureProject
{
    public const int MaxKeyLength = 100;
    public const int MaxNameLength = 200;
    public const int MaxSummaryLength = 2_000;

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Kind { get; private set; } = "other";
    public string? Repository { get; private set; }
    public string? Summary { get; private set; }
    public List<string> Keywords { get; private set; } = [];
    /// <summary>Commit do repositório de onde a engenharia reversa foi gerada (para saber se está desatualizada).</summary>
    public string? SourceCommit { get; private set; }
    public string? SourceBranch { get; private set; }
    public DateTimeOffset? SourceMappedAt { get; private set; }
    public int Order { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }

    public List<ArchitectureSection> Sections { get; private set; } = [];

    protected ArchitectureProject() { }

    public ArchitectureProject(string key, string actor, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Key = NormalizeKey(key);
        CreatedAt = now;
        CreatedBy = actor;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public void Update(string name, string? kind, string? repository, string? summary, IEnumerable<string>? keywords,
        string? sourceCommit, string? sourceBranch, int? order, string actor, DateTimeOffset now)
    {
        var cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length == 0) throw new DomainException("O nome do projeto é obrigatório.");
        if (cleanName.Length > MaxNameLength) throw new DomainException($"O nome pode ter no máximo {MaxNameLength} caracteres.");
        var cleanSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        if (cleanSummary?.Length > MaxSummaryLength)
            throw new DomainException($"O resumo do índice pode ter no máximo {MaxSummaryLength} caracteres (ele vai inteiro para o índice).");

        Name = cleanName;
        Kind = ArchitectureProjectKind.Normalize(kind);
        Repository = string.IsNullOrWhiteSpace(repository) ? null : repository.Trim();
        Summary = cleanSummary;
        if (keywords is not null)
            Keywords = keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(sourceCommit))
        {
            SourceCommit = sourceCommit.Trim();
            SourceBranch = string.IsNullOrWhiteSpace(sourceBranch) ? SourceBranch : sourceBranch.Trim();
            SourceMappedAt = now;
        }
        if (order is { } o) Order = o;
        IsDeleted = false;
        Touch(actor, now);
    }

    public void Touch(string actor, DateTimeOffset now)
    {
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public void Delete(string actor, DateTimeOffset now)
    {
        IsDeleted = true;
        Touch(actor, now);
    }

    public static string NormalizeKey(string? key)
    {
        var value = Regex.Replace((key ?? string.Empty).Trim().ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
        if (value.Length == 0) throw new DomainException("A chave é obrigatória (ex.: edv-solvace, revamp-actionplan).");
        if (value.Length > MaxKeyLength) throw new DomainException($"A chave pode ter no máximo {MaxKeyLength} caracteres.");
        return value;
    }
}
