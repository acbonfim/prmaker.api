namespace solvace.knowledge.domain.Entities;

/// <summary>Quem escreveu a versão de uma seção.</summary>
public static class ArchitectureSource
{
    public const string Skill = "skill";
    public const string Admin = "admin";
    public const string AI = "ai";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Skill, Admin, AI };

    public static string Normalize(string? source)
    {
        var value = (source ?? Admin).Trim().ToLowerInvariant();
        return All.Contains(value) ? value : Admin;
    }
}

/// <summary>
/// Para quem a seção foi escrita (0038): <c>llm</c> = técnica, lida pelas skills (vai para o espelho/índice);
/// <c>human</c> = o Guia em linguagem simples para QA, gestores e suporte — só na tela, na busca e no "Pergunte".
/// </summary>
public static class ArchitectureSectionAudience
{
    public const string Llm = "llm";
    public const string Human = "human";
    /// <summary>Prefixo das chaves do Guia (guia-o-que-e, guia-como-funciona...).</summary>
    public const string GuidePrefix = "guia-";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Llm, Human };

    public static string Normalize(string? audience)
    {
        var value = (audience ?? Llm).Trim().ToLowerInvariant();
        return All.Contains(value) ? value : throw new DomainException($"Público inválido: '{audience}' (llm ou human).");
    }

    /// <summary>Público de uma seção nova sem público informado: o Guia pela chave, senão técnica.</summary>
    public static string DefaultFor(string key) => key.StartsWith(GuidePrefix, StringComparison.Ordinal) ? Human : Llm;
}

/// <summary>Uma seção (markdown + mermaid) de um projeto da engenharia reversa; cada mudança vira uma versão.</summary>
public class ArchitectureSection
{
    public const int MaxTitleLength = 200;
    public const int MaxContentLength = 600_000; // 0052: documento da engenharia reversa (re-*) é profundo
    public const int MaxNoteLength = 500;

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string ContentHash { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public string Source { get; private set; } = ArchitectureSource.Admin;
    /// <summary>llm (exportada para as skills) | human (Guia, só na tela) — 0038.</summary>
    public string Audience { get; private set; } = ArchitectureSectionAudience.Llm;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    /// <summary>Vai para o espelho/índice das skills.</summary>
    public bool IsForLlm => Audience == ArchitectureSectionAudience.Llm;

    protected ArchitectureSection() { }

    public ArchitectureSection(Guid projectId, string key, string? audience = null)
    {
        Id = Guid.NewGuid();
        ProjectId = projectId;
        Key = ArchitectureProject.NormalizeKey(key);
        Audience = audience is null ? ArchitectureSectionAudience.DefaultFor(Key) : ArchitectureSectionAudience.Normalize(audience);
    }

    /// <summary>Muda o público (null mantém); devolve true se mudou.</summary>
    public bool SetAudience(string? audience)
    {
        if (audience is null) return false;
        var value = ArchitectureSectionAudience.Normalize(audience);
        if (value == Audience) return false;
        Audience = value;
        return true;
    }

    /// <summary>Grava o conteúdo; devolve a versão nova quando o conteúdo/título mudou (senão null — nada a versionar).</summary>
    public ArchitectureSectionVersion? Write(string? title, string content, int? order, string? source, string? note, string actor, DateTimeOffset now)
    {
        var cleanTitle = string.IsNullOrWhiteSpace(title) ? (Title.Length > 0 ? Title : Key) : title.Trim();
        if (cleanTitle.Length > MaxTitleLength) throw new DomainException($"O título pode ter no máximo {MaxTitleLength} caracteres.");
        var cleanContent = (content ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (cleanContent.Length == 0) throw new DomainException("O conteúdo da seção é obrigatório.");
        if (cleanContent.Length > MaxContentLength) throw new DomainException($"A seção pode ter no máximo {MaxContentLength} caracteres — divida em seções.");
        if (order is { } o) Order = o;

        var hash = KnowledgeArticle.Hash(cleanTitle + "\n" + cleanContent);
        if (hash == ContentHash) return null;

        Title = cleanTitle;
        Content = cleanContent;
        ContentHash = hash;
        Version++;
        Source = ArchitectureSource.Normalize(source);
        UpdatedAt = now;
        UpdatedBy = actor;
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (cleanNote?.Length > MaxNoteLength) cleanNote = cleanNote[..MaxNoteLength];
        return new ArchitectureSectionVersion(Id, Version, Title, Content, Source, cleanNote, actor, now);
    }
}

/// <summary>Histórico de uma seção (nada é sobrescrito sem ficar a versão anterior).</summary>
public class ArchitectureSectionVersion
{
    public Guid Id { get; private set; }
    public Guid SectionId { get; private set; }
    public int Version { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string Source { get; private set; } = ArchitectureSource.Admin;
    public string? Note { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    protected ArchitectureSectionVersion() { }

    public ArchitectureSectionVersion(Guid sectionId, int version, string title, string content, string source, string? note,
        string createdBy, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        SectionId = sectionId;
        Version = version;
        Title = title;
        Content = content;
        Source = source;
        Note = note;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }
}
