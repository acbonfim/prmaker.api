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

/// <summary>Tipos de interdependência (0034; 0066: cache, storage, job e trigger — mecanismos que a engenharia reversa descreve).</summary>
public static class ArchitectureRelationKind
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        "event", "queue", "database", "http", "package", "external", "frontend", "cache", "storage", "job", "trigger", "other"
    };

    public static string Normalize(string? kind)
    {
        var value = (kind ?? "other").Trim().ToLowerInvariant();
        return All.Contains(value) ? value : "other";
    }
}

/// <summary>Uma dependência de um projeto: alvo (chave do projeto ou <c>ext:&lt;serviço&gt;</c>), tipo, detalhe e evidência.</summary>
public class ArchitectureRelation
{
    public string Target { get; set; } = string.Empty;
    public string Kind { get; set; } = "other";
    public string? Detail { get; set; }
    public string? Evidence { get; set; }
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
    public const int MaxDisplayNameLength = 120;
    public const int MaxTaglineLength = 300;
    public const int MaxBusinessAreaLength = 80;

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
    /// <summary>Nome para pessoas ("Plano de Ação") — 0038; fora do espelho das skills.</summary>
    public string? DisplayName { get; private set; }
    /// <summary>Uma frase em linguagem simples sobre o que o projeto faz — 0038.</summary>
    public string? Tagline { get; private set; }
    /// <summary>Área de negócio que agrupa legado, revamp e front do mesmo módulo ("Plano de Ação", "Usuários e acesso") — 0038.</summary>
    public string? BusinessArea { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }

    public List<ArchitectureSection> Sections { get; private set; } = [];

    /// <summary>
    /// Interdependências com outros projetos/serviços (0034): evento, fila, banco, http, pacote, externo — com a evidência
    /// (arquivo/chave). Geradas pelo extrator da skill (mapear.py) ou editadas pelo admin.
    /// </summary>
    public List<ArchitectureRelation> Relations { get; private set; } = [];

    public const int MaxRelations = 300;

    /// <summary>Substitui as relações (null = mantém as atuais).</summary>
    /// <summary>
    /// 0070: cópia para leitura (cache por instância do repositório). Listas próprias — quem lê troca as relações e tira
    /// seções substituídas sem mexer no cache. As seções (cabeças) são as mesmas instâncias, que ninguém altera.
    /// </summary>
    public ArchitectureProject ReadCopy()
    {
        var copy = (ArchitectureProject)MemberwiseClone();
        copy.Sections = [.. Sections];
        copy.Relations = [.. Relations];
        copy.Keywords = [.. Keywords];
        return copy;
    }

    public void SetRelations(IEnumerable<ArchitectureRelation>? relations)
    {
        if (relations is null) return;
        var list = relations
            .Where(r => !string.IsNullOrWhiteSpace(r.Target) && !string.IsNullOrWhiteSpace(r.Kind))
            .Select(r => new ArchitectureRelation
            {
                Target = r.Target.Trim().ToLowerInvariant(),
                Kind = ArchitectureRelationKind.Normalize(r.Kind),
                Detail = Trim(r.Detail, 400),
                Evidence = Trim(r.Evidence, 400)
            })
            .Where(r => r.Target != Key)
            .DistinctBy(r => (r.Target, r.Kind, r.Detail))
            .ToList();
        if (list.Count > MaxRelations) throw new DomainException($"No máximo {MaxRelations} relações por projeto.");
        Relations = list;
    }

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length <= max ? v : v[..max];
    }

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

    /// <summary>Dados para pessoas (0038): null mantém, vazio limpa. Devolve true se algo mudou.</summary>
    public bool SetFriendly(string? displayName, string? tagline, string? businessArea)
    {
        var changed = false;
        DisplayName = Friendly(DisplayName, displayName, MaxDisplayNameLength, "O nome amigável", ref changed);
        Tagline = Friendly(Tagline, tagline, MaxTaglineLength, "A frase", ref changed);
        BusinessArea = Friendly(BusinessArea, businessArea, MaxBusinessAreaLength, "A área de negócio", ref changed);
        return changed;
    }

    private static string? Friendly(string? current, string? value, int max, string label, ref bool changed)
    {
        if (value is null) return current;
        var clean = value.Trim();
        if (clean.Length > max) throw new DomainException($"{label} pode ter no máximo {max} caracteres.");
        var result = clean.Length == 0 ? null : clean;
        if (result != current) changed = true;
        return result;
    }

    /// <summary>Acrescenta uma palavra-chave (0053: termo do glossário aceito na tela); false se já existia.</summary>
    public bool AddKeyword(string keyword)
    {
        var k = (keyword ?? string.Empty).Trim();
        if (k.Length == 0 || Keywords.Any(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase))) return false;
        Keywords = [.. Keywords, k];
        return true;
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
