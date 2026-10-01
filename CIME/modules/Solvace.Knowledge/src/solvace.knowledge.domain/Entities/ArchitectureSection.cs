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

/// <summary>Uma seção (markdown + mermaid) de um projeto da engenharia reversa; cada mudança vira uma versão.</summary>
public class ArchitectureSection
{
    public const int MaxTitleLength = 200;
    public const int MaxContentLength = 200_000;
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
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    protected ArchitectureSection() { }

    public ArchitectureSection(Guid projectId, string key)
    {
        Id = Guid.NewGuid();
        ProjectId = projectId;
        Key = ArchitectureProject.NormalizeKey(key);
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
