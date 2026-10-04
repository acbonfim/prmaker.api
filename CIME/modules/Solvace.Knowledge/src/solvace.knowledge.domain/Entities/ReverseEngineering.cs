using solvace.knowledge.domain.Reverse;

namespace solvace.knowledge.domain.Entities;

/// <summary>Onde está o código de um módulo: repositório (nome pelo remote, como no mapa da máquina), pasta e papel.</summary>
public class ReverseSource
{
    public string Repository { get; set; } = string.Empty;
    public string? Path { get; set; }
    /// <summary>backend | frontend | database | other</summary>
    public string Role { get; set; } = "backend";
    public string? Notes { get; set; }
}

/// <summary>
/// Módulo da engenharia reversa (0052) — um projeto da Base Solvace (legado ou revamp) com as fontes de código e os
/// apelidos que o campo "Module" dos cards usa ("Kaizen", "Users (Revamp)"). Criado sob demanda (primeira sessão ou
/// edição na tela); os documentos ficam nas revisões e, publicados, nas seções <c>re-*</c> do projeto.
/// </summary>
public class ReverseModule
{
    public const int MaxSources = 20;
    public const int MaxAliases = 30;

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public List<ReverseSource> Sources { get; private set; } = [];
    public List<string> Aliases { get; private set; } = [];
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    protected ReverseModule() { }

    public ReverseModule(string key, string actor, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Key = ArchitectureProject.NormalizeKey(key);
        CreatedAt = UpdatedAt = now;
        CreatedBy = UpdatedBy = actor;
    }

    /// <summary>null mantém o valor atual.</summary>
    public void Update(IEnumerable<ReverseSource>? sources, IEnumerable<string>? aliases, string? notes, string actor, DateTimeOffset now)
    {
        if (sources is not null)
        {
            var list = sources.Where(s => !string.IsNullOrWhiteSpace(s.Repository)).Select(s => new ReverseSource
            {
                Repository = s.Repository.Trim(),
                Path = string.IsNullOrWhiteSpace(s.Path) ? null : s.Path.Trim().Trim('/'),
                Role = s.Role?.Trim().ToLowerInvariant() is "frontend" or "database" or "other" ? s.Role.Trim().ToLowerInvariant() : "backend",
                Notes = string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes.Trim()[..Math.Min(s.Notes.Trim().Length, 300)]
            }).DistinctBy(s => (s.Repository.ToLowerInvariant(), s.Path)).ToList();
            if (list.Count > MaxSources) throw new DomainException($"No máximo {MaxSources} fontes por módulo.");
            Sources = list;
        }
        if (aliases is not null)
        {
            var list = aliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Where(a => a.Length <= 100)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count > MaxAliases) throw new DomainException($"No máximo {MaxAliases} apelidos por módulo.");
            Aliases = list;
        }
        if (notes is not null) Notes = notes.Trim().Length == 0 ? null : notes.Trim()[..Math.Min(notes.Trim().Length, 2000)];
        UpdatedAt = now;
        UpdatedBy = actor;
    }
}

public static class ReverseRevisionStatus
{
    public const string Draft = "draft";
    public const string Review = "review";
    public const string Changes = "changes";
    public const string Approved = "approved";
    public const string Published = "published";
    public const string Discarded = "discarded";
    public const string Superseded = "superseded";

    /// <summary>Revisão ainda aberta (uma por módulo + documento).</summary>
    public static bool IsOpen(string status) => status is Draft or Review or Changes or Approved;
}

public static class ReverseRevisionMode
{
    public static readonly IReadOnlySet<string> All = new HashSet<string> { "new", "improve", "redo", "manual" };
    public static string Normalize(string? mode) => All.Contains((mode ?? "new").Trim().ToLowerInvariant()) ? mode!.Trim().ToLowerInvariant() : "new";
}

/// <summary>
/// Uma revisão de um documento da engenharia reversa (0052): o rascunho de uma sessão do Claude (ou edição na tela) que
/// passa por revisão e, aprovada, é publicada como a seção <c>re-&lt;tipo&gt;</c>. Uma revisão aberta por módulo + documento.
/// </summary>
public class ReverseRevision
{
    public const int MaxContentLength = 600_000;
    public const int MaxSummaryLength = 4_000;
    public const int MaxNoteLength = 4_000;

    public Guid Id { get; private set; }
    public string ModuleKey { get; private set; } = string.Empty;
    public string DocType { get; private set; } = string.Empty;
    public int Number { get; private set; }
    public string Mode { get; private set; } = "new";
    public string Status { get; private set; } = ReverseRevisionStatus.Draft;
    public string Content { get; private set; } = string.Empty;
    public string? Summary { get; private set; }
    /// <summary>Resultado da checagem estrutural (JSON de <see cref="ReverseLintResult"/>).</summary>
    public string? Lint { get; private set; }
    /// <summary>Cobertura do inventário do código (JSON enviado pela skill: total, coberto, faltando).</summary>
    public string? Coverage { get; private set; }
    public double? CoverageRatio { get; private set; }
    /// <summary>Dados da sessão (JSON: commits das fontes, contagens do inventário, modelo).</summary>
    public string? Session { get; private set; }
    public string? ReviewNote { get; private set; }
    /// <summary>Versão da seção publicada a partir da qual o rascunho foi escrito (para avisar se mudou no meio).</summary>
    public int? BaseVersion { get; private set; }
    public int? PublishedVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;
    public DateTimeOffset? SubmittedAt { get; private set; }
    public string? SubmittedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public string? PublishedBy { get; private set; }

    protected ReverseRevision() { }

    public ReverseRevision(string moduleKey, string docType, int number, string? mode, string? initialContent, int? baseVersion, string actor, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ModuleKey = ArchitectureProject.NormalizeKey(moduleKey);
        DocType = ReverseDocTypes.Get(docType).Key;
        Number = number;
        Mode = ReverseRevisionMode.Normalize(mode);
        Content = (initialContent ?? string.Empty).Replace("\r\n", "\n");
        BaseVersion = baseVersion;
        CreatedAt = UpdatedAt = now;
        CreatedBy = UpdatedBy = actor;
    }

    public bool IsOpen => ReverseRevisionStatus.IsOpen(Status);

    /// <summary>
    /// Grava o conteúdo. Quem não aprova e mexe numa revisão em revisão/aprovada a devolve para rascunho (precisa reenviar);
    /// o revisor edita sem mudar o status.
    /// </summary>
    public void Save(string? content, string? summary, string? coverage, double? coverageRatio, string? session, bool byApprover, string actor, DateTimeOffset now)
    {
        if (!IsOpen) throw new DomainException($"A revisão #{Number} está {Label(Status)} — abra uma nova sessão para mudar o documento.");
        if (content is not null)
        {
            var clean = content.Replace("\r\n", "\n").Trim();
            if (clean.Length > MaxContentLength)
                throw new DomainException($"O documento pode ter no máximo {MaxContentLength} caracteres — tire repetições ou divida o módulo.");
            Content = clean;
        }
        if (summary is not null) Summary = Cap(summary, MaxSummaryLength);
        if (coverage is not null) Coverage = coverage.Trim().Length == 0 ? null : coverage;
        if (coverageRatio is { } r) CoverageRatio = Math.Clamp(r, 0, 1);
        if (session is not null) Session = session.Trim().Length == 0 ? null : session;
        if (!byApprover && Status is ReverseRevisionStatus.Review or ReverseRevisionStatus.Approved) Status = ReverseRevisionStatus.Draft;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public void SetLint(string lintJson) => Lint = lintJson;

    public void Submit(string actor, DateTimeOffset now)
    {
        if (Status is not (ReverseRevisionStatus.Draft or ReverseRevisionStatus.Changes))
            throw new DomainException($"Só rascunho ou revisão com ajustes pedidos vai para revisão (a #{Number} está {Label(Status)}).");
        if (Content.Trim().Length == 0) throw new DomainException("O documento está vazio.");
        Status = ReverseRevisionStatus.Review;
        SubmittedAt = now;
        SubmittedBy = actor;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    /// <summary>approve | changes | discard.</summary>
    public void Review(string action, string? note, string actor, DateTimeOffset now)
    {
        var a = (action ?? string.Empty).Trim().ToLowerInvariant();
        switch (a)
        {
            case "approve":
                if (Status is not (ReverseRevisionStatus.Review or ReverseRevisionStatus.Approved))
                    throw new DomainException($"Só revisão enviada pode ser aprovada (a #{Number} está {Label(Status)}).");
                Status = ReverseRevisionStatus.Approved;
                break;
            case "changes":
                if (Status is not (ReverseRevisionStatus.Review or ReverseRevisionStatus.Approved))
                    throw new DomainException($"Só revisão enviada pode receber pedido de ajustes (a #{Number} está {Label(Status)}).");
                if (string.IsNullOrWhiteSpace(note)) throw new DomainException("Diga o que precisa ser ajustado — a próxima sessão do Claude usa esta nota.");
                Status = ReverseRevisionStatus.Changes;
                break;
            case "discard":
                if (!IsOpen) throw new DomainException($"A revisão #{Number} já está {Label(Status)}.");
                Status = ReverseRevisionStatus.Discarded;
                break;
            default:
                throw new DomainException("Ação inválida: approve, changes ou discard.");
        }
        if (note is not null && note.Trim().Length > 0) ReviewNote = Cap(note, MaxNoteLength);
        ReviewedAt = now;
        ReviewedBy = actor;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public void MarkPublished(int sectionVersion, string actor, DateTimeOffset now)
    {
        if (Status != ReverseRevisionStatus.Approved)
            throw new DomainException($"Só revisão aprovada pode ser publicada (a #{Number} está {Label(Status)}).");
        Status = ReverseRevisionStatus.Published;
        PublishedVersion = sectionVersion;
        PublishedAt = now;
        PublishedBy = actor;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public void Supersede(DateTimeOffset now)
    {
        if (Status == ReverseRevisionStatus.Published) Status = ReverseRevisionStatus.Superseded;
        UpdatedAt = now;
    }

    public static string Label(string status) => status switch
    {
        ReverseRevisionStatus.Draft => "em rascunho",
        ReverseRevisionStatus.Review => "em revisão",
        ReverseRevisionStatus.Changes => "com ajustes pedidos",
        ReverseRevisionStatus.Approved => "aprovada",
        ReverseRevisionStatus.Published => "publicada",
        ReverseRevisionStatus.Discarded => "descartada",
        ReverseRevisionStatus.Superseded => "substituída",
        _ => status
    };

    private static string? Cap(string value, int max)
    {
        var v = value.Trim();
        return v.Length == 0 ? null : v.Length <= max ? v : v[..max];
    }
}

/// <summary>Anexo de UI/UX de um módulo (0052): link do Figma/protótipo ou arquivo (imagem, PDF, export do Figma).</summary>
public class ReverseAsset
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string> { "figma", "prototype", "link", "file", "image" };

    public Guid Id { get; private set; }
    public string ModuleKey { get; private set; } = string.Empty;
    public string Kind { get; private set; } = "link";
    public string Title { get; private set; } = string.Empty;
    public string? Url { get; private set; }
    public string? FileName { get; private set; }
    public string? ContentType { get; private set; }
    public long Size { get; private set; }
    public byte[]? Data { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>Telas que o anexo cobre (TELA-…), livre.</summary>
    public List<string> Screens { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }

    protected ReverseAsset() { }

    public static ReverseAsset Link(string moduleKey, string? kind, string title, string url, string? notes, IEnumerable<string>? screens, string actor, DateTimeOffset now)
    {
        var clean = (url ?? string.Empty).Trim();
        if (!Uri.TryCreate(clean, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new DomainException("Informe um link http(s) válido.");
        var k = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (!Kinds.Contains(k) || k is "file" or "image")
            k = uri.Host.Contains("figma.com", StringComparison.OrdinalIgnoreCase) ? "figma" : "link";
        return new ReverseAsset(moduleKey, k, title, notes, screens, actor, now) { Url = clean };
    }

    public static ReverseAsset File(string moduleKey, string title, string fileName, string contentType, byte[] data, string? notes, IEnumerable<string>? screens,
        string actor, DateTimeOffset now)
    {
        if (data.Length == 0) throw new DomainException("Arquivo vazio.");
        if (data.Length > MaxFileBytes) throw new DomainException($"O arquivo pode ter no máximo {MaxFileBytes / 1024 / 1024} MB.");
        var name = System.IO.Path.GetFileName((fileName ?? "arquivo").Replace('\\', '/'));
        var kind = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "image" : "file";
        return new ReverseAsset(moduleKey, kind, string.IsNullOrWhiteSpace(title) ? name : title, notes, screens, actor, now)
        {
            FileName = name, ContentType = contentType, Data = data, Size = data.Length
        };
    }

    private ReverseAsset(string moduleKey, string kind, string title, string? notes, IEnumerable<string>? screens, string actor, DateTimeOffset now)
    {
        var t = (title ?? string.Empty).Trim();
        if (t.Length == 0) throw new DomainException("Dê um título ao anexo (ex.: \"Figma — tela de cadastro\").");
        Id = Guid.NewGuid();
        ModuleKey = ArchitectureProject.NormalizeKey(moduleKey);
        Kind = kind;
        Title = t.Length <= 200 ? t : t[..200];
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()[..Math.Min(notes.Trim().Length, 2000)];
        Screens = (screens ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().Take(50).ToList();
        CreatedAt = now;
        CreatedBy = actor;
    }

    public void Delete() => IsDeleted = true;
}

/// <summary>
/// Item publicado da engenharia reversa no índice (0052): uma linha por item de cada documento publicado, para a busca
/// por item entre módulos (MCP <c>prmake_base_*</c>, tela Índice) sem ler o documento inteiro.
/// </summary>
public class ReverseIndexEntry
{
    public Guid Id { get; private set; }
    public string ModuleKey { get; private set; } = string.Empty;
    public string DocType { get; private set; } = string.Empty;
    public string ItemId { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public int Level { get; private set; }
    public int Order { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public List<string> Tags { get; private set; } = [];
    public List<string> Tables { get; private set; } = [];
    public List<string> Refs { get; private set; } = [];
    public List<string> Evidence { get; private set; } = [];
    public List<string> Modules { get; private set; } = [];
    public bool Removed { get; private set; }
    public int SectionVersion { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    protected ReverseIndexEntry() { }

    public ReverseIndexEntry(string moduleKey, string docType, ReverseItem item, int sectionVersion, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ModuleKey = moduleKey;
        DocType = docType;
        ItemId = item.Id;
        Kind = item.Kind;
        Title = item.Title;
        Level = item.Level;
        Order = item.Order;
        Body = item.Body;
        Tags = item.Tags;
        Tables = item.Tables;
        Refs = item.Refs;
        Evidence = item.Evidence;
        Modules = item.Modules;
        Removed = item.Removed;
        SectionVersion = sectionVersion;
        UpdatedAt = now;
    }

    public string Ref => $"{ModuleKey}#{ItemId}";
}

/// <summary>
/// Card analisado num módulo com engenharia reversa (0052): o <c>contexto</c> da analisar-bug registra os módulos; as
/// consultas à base com o card marcam <see cref="ConsultedAt"/>. A trava da etapa de investigação lê daqui.
/// </summary>
public class ReverseCardContext
{
    public const int MaxRefs = 60;

    public string CardNumber { get; private set; } = string.Empty;
    public List<string> Modules { get; private set; } = [];
    /// <summary>Algum dos módulos tem a engenharia reversa completa (todos os documentos exigidos publicados).</summary>
    public bool Complete { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ConsultedAt { get; private set; }
    public List<string> ConsultedRefs { get; private set; } = [];

    protected ReverseCardContext() { }

    public ReverseCardContext(string cardNumber, DateTimeOffset now)
    {
        CardNumber = (cardNumber ?? string.Empty).Trim();
        if (CardNumber.Length == 0) throw new DomainException("Informe o card.");
        CreatedAt = UpdatedAt = now;
    }

    public void SetModules(IEnumerable<string> modules, bool complete, DateTimeOffset now)
    {
        Modules = modules.Distinct().Take(10).ToList();
        Complete = complete;
        UpdatedAt = now;
    }

    public void Consulted(IEnumerable<string> refs, DateTimeOffset now)
    {
        ConsultedAt = now;
        ConsultedRefs = ConsultedRefs.Concat(refs.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()))
            .Distinct().TakeLast(MaxRefs).ToList();
        UpdatedAt = now;
    }
}
