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
    /// <summary>
    /// Termos do glossário publicado que não estão nos apelidos nem nas palavras-chave (0053) — sugestão na tela para
    /// virar apelido (campo Module do card) ou palavra-chave; nunca gravados sozinhos.
    /// </summary>
    public List<string> SuggestedTerms { get; private set; } = [];
    /// <summary>Termos que alguém dispensou (não voltam como sugestão).</summary>
    public List<string> DismissedTerms { get; private set; } = [];
    /// <summary>
    /// 0054: as armadilhas antigas (seção 090) e as sugestões sem item já foram ligadas aos itens da engenharia reversa —
    /// a seção antiga sai do espelho e a de armadilhas passa a ser gerada das <see cref="ReverseTrap"/>.
    /// </summary>
    public DateTimeOffset? TrapsMigratedAt { get; private set; }

    public void MarkTrapsMigrated(string actor, DateTimeOffset now)
    {
        TrapsMigratedAt = now;
        UpdatedAt = now;
        UpdatedBy = actor;
    }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    protected ReverseModule() { }

    public const int MaxSuggestedTerms = 300;

    /// <summary>Recalcula as sugestões a partir dos termos do glossário (o que já é apelido/palavra-chave/dispensado sai).</summary>
    public void SuggestTerms(IEnumerable<string> glossaryTerms, IEnumerable<string> keywords)
    {
        var known = Aliases.Concat(keywords).Concat(DismissedTerms).Select(Norm).ToHashSet();
        SuggestedTerms = glossaryTerms.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())
            .Where(t => t.Length is >= 2 and <= 100 && !known.Contains(Norm(t)))
            .DistinctBy(Norm).Take(MaxSuggestedTerms).ToList();
    }

    /// <summary>alias | keyword | dismiss. Devolve o termo aceito (para virar palavra-chave no projeto) ou null.</summary>
    public string? ResolveTerm(string term, string action, string actor, DateTimeOffset now)
    {
        var found = SuggestedTerms.FirstOrDefault(t => Norm(t) == Norm(term)) ?? term?.Trim();
        if (string.IsNullOrWhiteSpace(found)) throw new DomainException("Informe o termo.");
        SuggestedTerms = SuggestedTerms.Where(t => Norm(t) != Norm(found)).ToList();
        string? keyword = null;
        switch ((action ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "alias":
                if (Aliases.Count >= MaxAliases) throw new DomainException($"No máximo {MaxAliases} apelidos por módulo.");
                if (!Aliases.Any(a => Norm(a) == Norm(found))) Aliases = [.. Aliases, found];
                break;
            case "keyword":
                keyword = found;
                break;
            case "dismiss":
                if (!DismissedTerms.Any(d => Norm(d) == Norm(found))) DismissedTerms = [.. DismissedTerms, found];
                break;
            default:
                throw new DomainException("Ação inválida: alias, keyword ou dismiss.");
        }
        UpdatedAt = now;
        UpdatedBy = actor;
        return keyword;
    }

    private static string Norm(string? value) => ReverseLint.Normalize(value ?? string.Empty).Trim();

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

    /// <summary>
    /// 0056: mesma classe de bug do construtor de <see cref="ReverseTrap"/> — "new" (o default) É um valor válido de
    /// <see cref="All"/>, então um "mode" nulo caía no ramo que tentava usar "mode!" (ainda null) e derrubava com
    /// NullReferenceException. Hoje nenhuma chamada passa null (o único construtor normaliza antes), mas o parâmetro
    /// é público e nullable — melhor corrigir do que deixar a próxima chamada cair nisso.
    /// </summary>
    public static string Normalize(string? mode)
    {
        var normalized = (mode ?? "new").Trim().ToLowerInvariant();
        return All.Contains(normalized) ? normalized : "new";
    }
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
    /// <summary>
    /// Decisões da sessão sobre as sugestões do pacote (0053): JSON de <see cref="ReverseSuggestionDecision"/> — aplicada
    /// (itens que mudaram) ou recusada (motivo). Resolvidas na publicação.
    /// </summary>
    public string? SuggestionDecisions { get; private set; }

    public void SetSuggestionDecisions(string? json)
    {
        if (!IsOpen) throw new DomainException($"A revisão #{Number} está {Label(Status)}.");
        SuggestionDecisions = string.IsNullOrWhiteSpace(json) ? null : json;
    }

    /// <summary>Andamento da sessão do Claude (JSON de <see cref="ReverseProgress"/>) — a tela mostra ao vivo.</summary>
    public string? Progress { get; private set; }
    public DateTimeOffset? ProgressAt { get; private set; }
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
        Progress = ReverseProgress.Initial(now).Serialize();
        ProgressAt = now;
    }

    /// <summary>Etapa/atividade/registro da sessão (só com a revisão aberta).</summary>
    public ReverseProgress ReportProgress(ReverseProgressUpdate update, DateTimeOffset now)
    {
        if (!IsOpen) throw new DomainException($"A revisão #{Number} está {Label(Status)} — o andamento é da sessão aberta.");
        var progress = ReverseProgress.Parse(Progress, now);
        progress.Apply(update, now);
        Progress = progress.Serialize();
        ProgressAt = now;
        return progress;
    }

    /// <summary>Retomada: nova sessão do Claude na mesma revisão — registra no andamento.</summary>
    public void Resumed(string actor, DateTimeOffset now) =>
        ReportProgress(new ReverseProgressUpdate { Log = $"Sessão retomada por {actor}", Kind = "progress" }, now);

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
        var progress = ReverseProgress.Parse(Progress, now);
        foreach (var step in progress.Steps.Where(s => s.Key is "checagem" or "envio" && s.Status != "completed"))
        {
            step.Status = "completed";
            step.FinishedAt = now;
        }
        progress.Activity = null;
        progress.Logs.Add(new ReverseProgressLog { At = now, Text = $"Enviado para revisão por {actor}", Kind = "progress" });
        Progress = progress.Serialize();
        ProgressAt = now;
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

/// <summary>
/// Armadilha (0054): o que a operação ensinou sobre um módulo — o que quebra, causa raiz já vista, configuração que
/// confunde, consulta de diagnóstico — ligada aos itens da engenharia reversa (<c>RN-020</c>, <c>TELA-003</c>). A
/// engenharia reversa descreve "como o sistema é"; a armadilha, "o que já deu errado" — as análises recebem as duas.
/// Criada pela análise/migração = a conferir; por aprovador (ou conferida) = confirmada.
/// </summary>
public class ReverseTrap
{
    public const int MaxTextLength = 6_000;
    public static readonly IReadOnlySet<string> Origins = new HashSet<string> { "manual", "suggestion", "migrated", "learning" };

    public Guid Id { get; private set; }
    public string ModuleKey { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public List<string> Items { get; private set; } = [];
    public List<string> Cards { get; private set; } = [];
    public string Origin { get; private set; } = "manual";
    public bool NeedsReview { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }

    protected ReverseTrap() { }

    public ReverseTrap(string moduleKey, string title, string text, IEnumerable<string>? items, IEnumerable<string>? cards, string? origin, bool needsReview,
        string actor, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ModuleKey = ArchitectureProject.NormalizeKey(moduleKey);
        // 0056: bug encontrado ao testar — o "!" não troca o null pelo default em tempo de execução; "origin" nulo
        // (ex.: POST /traps sem o campo, opcional) derrubava com NullReferenceException. Usa o valor já normalizado.
        var normalizedOrigin = (origin ?? "manual").Trim().ToLowerInvariant();
        Origin = Origins.Contains(normalizedOrigin) ? normalizedOrigin : "manual";
        CreatedAt = UpdatedAt = now;
        CreatedBy = UpdatedBy = actor;
        Edit(title, text, items, cards, actor, now);
        NeedsReview = needsReview;
    }

    public void Edit(string? title, string? text, IEnumerable<string>? items, IEnumerable<string>? cards, string actor, DateTimeOffset now)
    {
        if (title is not null)
        {
            var t = title.Trim();
            if (t.Length == 0) throw new DomainException("Dê um título à armadilha (o que dá errado, numa frase).");
            Title = t.Length <= 200 ? t : t[..200];
        }
        if (text is not null)
        {
            var x = text.Replace("\r\n", "\n").Trim();
            if (x.Length == 0) throw new DomainException("Descreva a armadilha (sintoma, causa, como diagnosticar).");
            if (x.Length > MaxTextLength) throw new DomainException($"A armadilha pode ter no máximo {MaxTextLength} caracteres.");
            Text = x;
        }
        if (items is not null)
            Items = items.Select(i => Reverse.ReverseItemKinds.ParseRef(i)).Where(r => r is not null)
                .Select(r => r!.Value.Module is null ? r.Value.Id : $"{r.Value.Module}#{r.Value.Id}").Distinct().Take(30).ToList();
        if (cards is not null)
            Cards = cards.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().Take(30).ToList();
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public void Confirm(string actor, DateTimeOffset now)
    {
        NeedsReview = false;
        ReviewedBy = actor;
        ReviewedAt = now;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public void Delete(string actor, DateTimeOffset now)
    {
        IsDeleted = true;
        UpdatedAt = now;
        UpdatedBy = actor;
    }
}

/// <summary>Decisão da sessão sobre uma sugestão do pacote (0053).</summary>
public class ReverseSuggestionDecision
{
    public Guid SuggestionId { get; set; }
    /// <summary>applied | dismissed</summary>
    public string Decision { get; set; } = string.Empty;
    /// <summary>Itens que mudaram por causa da sugestão (aplicada).</summary>
    public List<string> Items { get; set; } = [];
    /// <summary>Resumo do que foi feito ou o motivo da recusa.</summary>
    public string? Note { get; set; }

    public static List<ReverseSuggestionDecision> Normalize(IEnumerable<ReverseSuggestionDecision>? decisions)
    {
        var result = new List<ReverseSuggestionDecision>();
        foreach (var d in decisions ?? [])
        {
            if (d.SuggestionId == Guid.Empty || result.Any(r => r.SuggestionId == d.SuggestionId)) continue;
            var decision = (d.Decision ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "applied" or "aplicada" or "apply" => "applied",
                "dismissed" or "recusada" or "recusar" or "dismiss" => "dismissed",
                var other => throw new DomainException($"Decisão inválida para a sugestão {d.SuggestionId}: '{other}' (applied ou dismissed).")
            };
            var note = string.IsNullOrWhiteSpace(d.Note) ? null : d.Note.Trim()[..Math.Min(d.Note.Trim().Length, 480)];
            if (decision == "dismissed" && note is null) throw new DomainException($"Diga o motivo da recusa da sugestão {d.SuggestionId}.");
            result.Add(new ReverseSuggestionDecision
            {
                SuggestionId = d.SuggestionId, Decision = decision, Note = note,
                Items = (d.Items ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct().Take(50).ToList()
            });
        }
        return result;
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
    /// <summary>Sinônimos (itens GLO — 0053): expansão da busca do módulo.</summary>
    public List<string> Synonyms { get; private set; } = [];
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
        Synonyms = item.Synonyms;
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
