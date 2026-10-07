using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Responses;
using solvace.knowledge.domain.Reverse;

namespace solvace.knowledge.application;

/// <summary>
/// Engenharia reversa por módulo (0052): cada módulo (projeto legado/revamp da Base Solvace) tem os documentos de
/// <see cref="ReverseDocTypes"/>. A skill abre uma sessão (rascunho), envia para revisão, um aprovador aprova e publica:
/// o documento vira a seção <c>re-&lt;tipo&gt;</c> do projeto (espelho, busca, Pergunte) e os itens com ID vão para o
/// índice por item, que as análises consultam pelo MCP sem ler o documento inteiro nem o código.
/// </summary>
public partial class ReverseEngineeringApplication(IKnowledgeRepository repository, IReverseSettingsProvider settingsProvider) : IReverseEngineeringApplication
{
    /// <summary>Tipos de projeto que são "módulo" na tela (as visões transversais ficam de fora).</summary>
    public static readonly IReadOnlySet<string> ModuleKinds = new HashSet<string> { "legacy", "revamp", "frontend", "integration", "auth" };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ── Configuração e tipos ────────────────────────────────────────────────────────────────────

    public async Task<ReverseSettingsResponse> GetSettingsAsync(IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        return new ReverseSettingsResponse
        {
            ApproverRoles = s.ApproverRoles.ToList(), RequiredDocs = s.RequiredDocs.ToList(), GateStep = s.GateStep, MinCoverage = s.MinCoverage,
            CanApprove = CanApprove(s, userRoles),
            Kinds = ReverseItemKinds.All.Select(k => new ReverseItemKindResponse { Prefix = k.Prefix, Label = k.Label, Plural = k.Plural }).ToList(),
            ReferenceDatabase = ToReference(s.ReferenceDatabase),
            GlossaryExclusions = (s.GlossaryExclusions ?? ReverseSettings.DefaultGlossaryExclusions).ToList(),
            Translations = ParseJson(s.Translations ?? ReverseSettings.DefaultTranslations),
            Infra = ParseJson(s.Infra ?? ReverseSettings.DefaultInfra),
            Generation = ParseJson(s.Generation ?? ReverseSettings.DefaultGeneration)
        };
    }

    private static System.Text.Json.JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { using var doc = System.Text.Json.JsonDocument.Parse(json); return doc.RootElement.Clone(); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static ReverseReferenceDatabaseResponse? ToReference(ReverseReferenceDatabase? r) => r is null ? null : new()
    {
        Environment = r.Environment, Host = r.Host, Global = r.Global, Locals = r.Locals.ToList()
    };

    public static bool CanApprove(ReverseSettings settings, IReadOnlyCollection<string> userRoles) =>
        userRoles.Any(r => settings.ApproverRoles.Contains(r, StringComparer.OrdinalIgnoreCase));

    public async Task<List<ReverseDocTypeResponse>> GetDocTypesAsync(CancellationToken cancellationToken, bool withTemplate = true)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        return ReverseDocTypes.All.Select(t => ToDocType(t, s, withTemplate)).ToList();
    }

    private static ReverseDocTypeResponse ToDocType(ReverseDocType t, ReverseSettings s, bool withTemplate = true) => new()
    {
        Key = t.Key, Title = t.Title, SectionKey = t.SectionKey, Order = t.Order, Required = s.RequiredDocs.Contains(t.Key), Kinds = t.Kinds.ToList(),
        Headings = t.Headings.Select(h => h.Title).ToList(), Purpose = t.Purpose,
        // 0070: a tela não usa o modelo (32 KB por chamada); a skill continua recebendo
        Template = !withTemplate ? string.Empty
            : (s.Templates.TryGetValue(t.Key, out var custom) && !string.IsNullOrWhiteSpace(custom) ? custom.Trim() : t.Template.Trim())
              + (t.Derived ? "" : "\n\n" + ReverseDocTypes.ItemGuide.Trim())
    };

    // ── Módulos ─────────────────────────────────────────────────────────────────────────────────

    public async Task<List<ReverseModuleSummaryResponse>> ListModulesAsync(CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var projects = (await repository.GetProjectHeadsAsync(cancellationToken)).Where(p => ModuleKinds.Contains(p.Kind)).ToList();
        var modules = (await repository.GetReverseModulesAsync(cancellationToken)).ToDictionary(m => m.Key);
        var open = WithProgress(await repository.GetRevisionHeadsAsync(null, null, OpenStatuses, cancellationToken))
            .GroupBy(r => (r.ModuleKey, r.DocType)).ToDictionary(g => g.Key, g => g.First());
        // 0070: contagens no banco — antes carregava o índice inteiro (texto de todos os itens) e 500 sugestões só para contar
        var items = (await repository.CountIndexItemsAsync(null, cancellationToken)).GroupBy(c => c.ModuleKey).ToDictionary(g => g.Key, g => g.Sum(c => c.Count));
        var suggestions = await repository.CountPendingSuggestionsAsync(null, cancellationToken);

        return projects
            .OrderBy(p => p.BusinessArea ?? p.DisplayName ?? p.Name).ThenBy(p => p.Kind).ThenBy(p => p.Name)
            .Select(p =>
            {
                var summary = new ReverseModuleSummaryResponse();
                FillSummary(summary, p, modules.GetValueOrDefault(p.Key), s, open, items.GetValueOrDefault(p.Key), suggestions);
                return summary;
            }).ToList();
    }

    /// <summary>Andamento das revisões abertas (o JSON vem cru do banco).</summary>
    private static List<ReverseRevisionHead> WithProgress(List<ReverseRevisionHead> heads)
    {
        foreach (var h in heads)
        {
            if (h.ProgressRaw is not null && ReverseRevisionStatus.IsOpen(h.Status))
                h.Progress = ReverseProgress.Parse(h.ProgressRaw, h.ProgressAt ?? h.UpdatedAt);
            h.ProgressRaw = null;
        }
        return heads;
    }

    private static readonly string[] OpenStatuses =
        [ReverseRevisionStatus.Draft, ReverseRevisionStatus.Review, ReverseRevisionStatus.Changes, ReverseRevisionStatus.Approved];

    private static void FillSummary(ReverseModuleSummaryResponse summary, ArchitectureProject p, ReverseModule? module, ReverseSettings s,
        IReadOnlyDictionary<(string, string), ReverseRevisionHead> open, int items, IReadOnlyDictionary<(string, string), int> suggestions)
    {
        summary.Key = p.Key;
        summary.Name = p.Name;
        summary.DisplayName = p.DisplayName;
        summary.BusinessArea = p.BusinessArea;
        summary.ProjectKind = p.Kind;
        summary.World = World(p.Kind);
        summary.Aliases = module?.Aliases ?? [];
        summary.Items = items;
        summary.Docs = ReverseDocTypes.All.Select(t =>
        {
            var section = p.Sections.FirstOrDefault(x => x.Key == t.SectionKey);
            var head = open.GetValueOrDefault((p.Key, t.Key));
            return new ReverseDocStatusResponse
            {
                Type = t.Key, Title = t.Title, Required = s.RequiredDocs.Contains(t.Key),
                Published = section is null ? null : new ReversePublishedInfo
                {
                    Version = section.Version, UpdatedAt = section.UpdatedAt, UpdatedBy = section.UpdatedBy, Length = section.Length
                },
                Open = head,
                State = head?.Status ?? (section is null ? "none" : ReverseRevisionStatus.Published),
                PendingSuggestions = suggestions.GetValueOrDefault((p.Key, t.SectionKey))
            };
        }).ToList();
        // 0054: a visão prática só começa com os demais exigidos publicados e fica desatualizada quando um técnico é republicado
        foreach (var doc in summary.Docs.Where(d => ReverseDocTypes.ByKey[d.Type].Derived))
        {
            doc.BlockedBy = s.RequiredDocs.Where(r => !ReverseDocTypes.ByKey.TryGetValue(r, out var rt) || !rt.Derived)
                .Where(r => summary.Docs.FirstOrDefault(x => x.Type == r)?.Published is null).ToList();
            if (doc.Published is { } practical)
                doc.StaleBecause = summary.Docs.Where(x => x.Type != doc.Type && x.Published is not null && x.Published.UpdatedAt > practical.UpdatedAt)
                    .Select(x => x.Title).ToList();
        }
        summary.RequiredCount = s.RequiredDocs.Count;
        summary.PublishedRequired = summary.Docs.Count(d => d.Required && d.Published is not null);
        summary.Complete = summary.RequiredCount > 0 && summary.PublishedRequired == summary.RequiredCount;
        summary.InReview = summary.Docs.Count(d => d.Open?.Status is ReverseRevisionStatus.Review or ReverseRevisionStatus.Approved);
    }

    public static string World(string kind) => kind switch
    {
        "legacy" => "legado",
        "revamp" => "revamp",
        "frontend" => "front",
        "integration" => "integração",
        "auth" => "login",
        _ => kind
    };

    public async Task<ReverseModuleResponse> GetModuleAsync(string key, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var all = await repository.GetProjectHeadsAsync(cancellationToken);
        var modules = await repository.GetReverseModulesAsync(cancellationToken);
        await ReverseRelations.ApplyAsync(repository, all, modules, cancellationToken); // 0066: INT resolvidos
        var project = FindProject(all, key);
        var module = modules.FirstOrDefault(m => m.Key == project.Key);
        var open = WithProgress(await repository.GetRevisionHeadsAsync(project.Key, null, OpenStatuses, cancellationToken))
            .GroupBy(r => (r.ModuleKey, r.DocType)).ToDictionary(g => g.Key, g => g.First());
        // 0070: contagens no banco (antes: o índice inteiro de todos os módulos em memória, 8–30 s numa instância nova)
        var counts = await repository.CountIndexItemsAsync(project.Key, cancellationToken);
        var suggestions = await repository.CountPendingSuggestionsAsync(project.Key, cancellationToken);

        var response = new ReverseModuleResponse();
        FillSummary(response, project, module, s, open, counts.Sum(c => c.Count), suggestions);
        foreach (var doc in response.Docs.Where(d => d.Published is not null))
            doc.Published!.Items = counts.Where(c => c.DocType == doc.Type).Sum(c => c.Count);
        response.Repository = project.Repository;
        response.Summary = project.Summary;
        response.Configured = module is not null;
        response.Sources = module?.Sources ?? DefaultSources(project);
        response.Notes = module?.Notes;
        response.Assets = WithDownload(await repository.GetAssetHeadsAsync(project.Key, cancellationToken));
        response.ItemsByKind = counts.GroupBy(c => c.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(c => c.Count));
        response.Relations = project.Relations;
        response.UsedBy = all.Where(p => p.Key != project.Key)
            .SelectMany(p => p.Relations.Where(r => r.Target == project.Key)
                .Select(r => new ArchitectureIncomingRelation { Source = p.Key, Kind = r.Kind, Detail = r.Detail, Evidence = r.Evidence }))
            .ToList();
        response.Siblings = project.BusinessArea is null
            ? []
            : all.Where(p => p.Key != project.Key && p.BusinessArea == project.BusinessArea && ModuleKinds.Contains(p.Kind)).Select(p => p.Key).ToList();
        response.CanApprove = CanApprove(s, userRoles);
        response.SuggestedTerms = module?.SuggestedTerms ?? [];
        var (traps, toReview) = await repository.CountTrapsAsync(project.Key, cancellationToken);
        response.Traps = traps;
        response.TrapsToReview = toReview;
        response.TrapsMigratedAt = module?.TrapsMigratedAt;
        response.SupersededSections = ReverseSupersession.Compute(project, s, module);
        return response;
    }

    /// <summary>Termo sugerido pelo glossário (0053): vira apelido do módulo, palavra-chave do projeto ou é dispensado.</summary>
    public async Task<ReverseModuleResponse> ResolveTermAsync(string key, ResolveReverseTermRequest request, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken)
    {
        await RequireApproverAsync(userRoles, "aceitar termos do glossário", cancellationToken);
        var normalized = ArchitectureProject.NormalizeKey(key);
        var project = await repository.GetProjectForUpdateAsync(normalized, cancellationToken);
        if (project is null || project.IsDeleted) throw new KnowledgeNotFoundException($"Projeto '{key}' não encontrado.");
        var module = await EnsureModuleAsync(project, actor, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (module.ResolveTerm(request.Term, request.Action, actor, now) is { } keyword && project.AddKeyword(keyword))
            project.Touch(actor, now);
        await repository.SaveChangesAsync(cancellationToken);
        return await GetModuleAsync(project.Key, userRoles, cancellationToken);
    }

    /// <summary>Fontes sugeridas enquanto o módulo não foi configurado: o repositório do projeto (nome pela URL).</summary>
    public static List<ReverseSource> DefaultSources(ArchitectureProject project)
    {
        var repo = RepoName(project.Repository);
        if (repo is null && project.Kind == "legacy") repo = "edv-solvace";
        return repo is null ? [] : [new ReverseSource { Repository = repo, Role = "backend", Notes = "sugerido pelo projeto — confirme" }];
    }

    private static string? RepoName(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository)) return null;
        var value = repository.Trim().TrimEnd('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        var slash = value.LastIndexOfAny(['/', ':']);
        return slash >= 0 ? value[(slash + 1)..] : value;
    }

    private static List<ReverseAssetResponse> WithDownload(List<ReverseAssetResponse> assets)
    {
        foreach (var a in assets.Where(a => a.FileName is not null)) a.Download = $"ReverseEngineering/assets/{a.Id}/file";
        return assets;
    }

    public async Task<ReverseModuleResponse> UpsertModuleAsync(string key, UpsertReverseModuleRequest request, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken)
    {
        await RequireApproverAsync(userRoles, "configurar o módulo", cancellationToken);
        var project = await ProjectAsync(key, cancellationToken);
        var module = await EnsureModuleAsync(project, actor, cancellationToken);
        module.Update(request.Sources, request.Aliases, request.Notes, actor, DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        return await GetModuleAsync(project.Key, userRoles, cancellationToken);
    }

    private async Task<ReverseModule> EnsureModuleAsync(ArchitectureProject project, string actor, CancellationToken cancellationToken)
    {
        var module = await repository.GetReverseModuleForUpdateAsync(project.Key, cancellationToken);
        if (module is not null) return module;
        module = new ReverseModule(project.Key, actor, DateTimeOffset.UtcNow);
        module.Update(DefaultSources(project), DefaultAliases(project), null, actor, DateTimeOffset.UtcNow);
        repository.AddReverseModule(module);
        return module;
    }

    /// <summary>Apelidos iniciais: nome amigável e área de negócio (o campo Module do card costuma usar esses nomes).</summary>
    private static List<string> DefaultAliases(ArchitectureProject p) =>
        new[] { p.DisplayName, p.Kind == "revamp" && p.DisplayName is not null ? $"{p.DisplayName} (Revamp)" : null }
            .Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!).ToList();

    // ── Documentos publicados ───────────────────────────────────────────────────────────────────

    public async Task<ReverseDocResponse> GetDocAsync(string key, string docType, CancellationToken cancellationToken, bool withContent = true)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var type = ReverseDocTypes.Get(docType);
        // 0070: o projeto sem o texto; só a seção pedida vem com texto (antes, todas as seções do módulo) e os itens saem do
        // índice (antes, o documento era relido a cada chamada). Sem o texto (?content=false), vai o sumário em pedaços.
        var project = await repository.GetProjectHeadAsync(ArchitectureProject.NormalizeKey(key), cancellationToken) ?? throw NotFound(key);
        var section = project.Sections.FirstOrDefault(x => x.Key == type.SectionKey);
        string? content = section is not null && withContent
            ? (await repository.GetSectionContentsAsync([section.Id], cancellationToken)).GetValueOrDefault(section.Id)
            : null;
        List<ReverseItemHead> items = [];
        if (section is not null)
        {
            items = (await repository.GetIndexHeadsAsync(project.Key, type.Key, cancellationToken))
                .Select(e => new ReverseItemHead { Id = e.ItemId, Kind = e.Kind, Title = e.Title, Level = e.Level, Removed = e.Removed }).ToList();
            if (items.Count == 0)
            {
                // índice vazio (documento sem itens ou anterior ao índice): lê do texto, como antes
                content ??= (await repository.GetSectionContentsAsync([section.Id], cancellationToken)).GetValueOrDefault(section.Id);
                items = ReverseDocParser.Parse(content ?? string.Empty).Select(ToItemHead).ToList();
                if (!withContent) content = null;
            }
        }
        return new ReverseDocResponse
        {
            ModuleKey = project.Key,
            Type = ToDocType(type, s),
            Content = content,
            Outline = section is null || withContent ? null : ArchitectureApplication.ToOutline(section, await SectionOutlines.GetAsync(repository, section, cancellationToken)),
            Published = section is null ? null : new ReversePublishedInfo
            {
                Version = section.Version, UpdatedAt = section.UpdatedAt, UpdatedBy = section.UpdatedBy, Length = section.Length,
                Items = items.Count(i => !i.Removed)
            },
            Items = items,
            Revisions = WithProgress(await repository.GetRevisionHeadsAsync(project.Key, type.Key, null, cancellationToken)),
            Suggestions = (await repository.GetSuggestionsAsync(ArchitectureSuggestionStatus.Pending, cancellationToken))
                .Where(x => x.ProjectKey == project.Key && x.SectionKey == type.SectionKey).Select(ToSuggestion).ToList()
        };
    }

    private static ReverseItemHead ToItemHead(ReverseItem i) => new() { Id = i.Id, Kind = i.Kind, Title = i.Title, Level = i.Level, Removed = i.Removed };

    // ── Sessão (rascunho) e revisões ────────────────────────────────────────────────────────────

    public async Task<ReverseSessionResponse> StartSessionAsync(string key, string docType, StartReverseSessionRequest request, string actor,
        IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var type = ReverseDocTypes.Get(docType);
        // 0070: os outros projetos sem o texto (antes, a Base inteira); só o do módulo vem com as seções completas
        var all = await repository.GetProjectHeadsAsync(cancellationToken);
        await ReverseRelations.ApplyAsync(repository, all, null, cancellationToken); // 0066: relacionados pelos INT resolvidos
        var head = FindProject(all, key);
        var project = await repository.GetProjectAsync(head.Key, cancellationToken) ?? head;
        project.SetRelations(head.Relations);
        all[all.IndexOf(head)] = project;
        await EnsureModuleAsync(project, actor, cancellationToken);
        var section = project.Sections.FirstOrDefault(x => x.Key == type.SectionKey);

        if (type.Derived)
        {
            var missing = s.RequiredDocs.Where(r => r != type.Key && ReverseDocTypes.ByKey.TryGetValue(r, out var rt) && !rt.Derived
                                                    && !project.Sections.Any(x => x.Key == rt.SectionKey)).ToList();
            if (missing.Count > 0)
                throw new DomainException($"A {type.Title} é escrita só do que foi publicado: publique antes {string.Join(", ", missing)}.");
        }
        var revision = await repository.GetOpenRevisionForUpdateAsync(project.Key, type.Key, cancellationToken);
        var resumed = revision is not null;
        if (revision is not null && revision.Status is ReverseRevisionStatus.Draft or ReverseRevisionStatus.Changes)
            revision.Resumed(actor, DateTimeOffset.UtcNow);
        if (revision is null)
        {
            var mode = ReverseRevisionMode.Normalize(request.Mode ?? (section is null ? "new" : "improve"));
            var number = await repository.GetMaxRevisionNumberAsync(project.Key, type.Key, cancellationToken) + 1;
            revision = new ReverseRevision(project.Key, type.Key, number, mode, mode == "improve" ? section?.Content : null, section?.Version, actor, DateTimeOffset.UtcNow);
            repository.AddRevision(revision);
        }
        await repository.SaveChangesAsync(cancellationToken);

        var entries = await ReverseSearch.EntriesAsync(repository, cancellationToken);
        var lastChanges = (await repository.GetRevisionHeadsAsync(project.Key, type.Key, null, cancellationToken))
            .FirstOrDefault(r => r.ReviewNote is not null && r.Status is ReverseRevisionStatus.Changes or ReverseRevisionStatus.Discarded or ReverseRevisionStatus.Review);
        return new ReverseSessionResponse
        {
            Revision = await ToRevisionAsync(revision, project, s, userRoles, withDiff: false, cancellationToken),
            Resumed = resumed,
            Module = await GetModuleAsync(project.Key, userRoles, cancellationToken),
            DocType = ToDocType(type, s),
            Published = section?.Content,
            PublishedVersion = section?.Version,
            Suggestions = (await repository.GetSuggestionsAsync(ArchitectureSuggestionStatus.Pending, cancellationToken))
                .Where(x => x.ProjectKey == project.Key && ForDocument(x, type)).Take(40).Select(ToSuggestion).ToList(),
            ReviewNote = revision.ReviewNote ?? lastChanges?.ReviewNote,
            OtherDocIds = OtherDocIds(entries, project.Key, type.Key),
            Related = RenderRelated(project, all, entries),
            ExistingSections = project.Sections.Where(x => x.IsForLlm && !x.Key.StartsWith(ReverseDocTypes.SectionPrefix, StringComparison.Ordinal))
                .OrderBy(x => x.Order)
                .Select(x => new ArchitectureSectionSummaryResponse { Id = x.Id, Key = x.Key, Title = x.Title, Order = x.Order, Version = x.Version, Length = x.Length })
                .ToList(),
            MinCoverage = s.MinCoverage,
            ReferenceDatabase = ToReference(s.ReferenceDatabase),
            GlossaryExclusions = (s.GlossaryExclusions ?? ReverseSettings.DefaultGlossaryExclusions).ToList(),
            PublishedSession = Element((await repository.GetPublishedRevisionsForUpdateAsync(project.Key, type.Key, cancellationToken))
                .OrderByDescending(r => r.Number).FirstOrDefault()?.Session),
            PublishedDocs = type.Derived
                ? ReverseDocTypes.All.Where(t => !t.Derived).Select(t => (t.Key, Section: project.Sections.FirstOrDefault(x => x.Key == t.SectionKey)))
                    .Where(x => x.Section is not null).ToDictionary(x => x.Key, x => x.Section!.Content)
                : [],
            Questions = await ModuleQuestionsAsync(project, module: (await repository.GetReverseModulesAsync(cancellationToken)).FirstOrDefault(m => m.Key == project.Key), cancellationToken),
            Traps = (await repository.GetTrapsAsync(project.Key, cancellationToken)).Select(ReverseTrapResponse.From).ToList(),
            LegacyTraps = (await repository.GetReverseModulesAsync(cancellationToken)).FirstOrDefault(m => m.Key == project.Key)?.TrapsMigratedAt is null
                ? project.Sections.FirstOrDefault(x => x.Key == "armadilhas")?.Content
                : null
        };
    }

    /// <summary>0054: perguntas do "Pergunte" sobre o módulo (pela seção sugerida/respondida ou pelo nome/apelido/sigla no texto).</summary>
    private async Task<List<ReverseModuleQuestion>> ModuleQuestionsAsync(ArchitectureProject project, ReverseModule? module, CancellationToken cancellationToken)
    {
        var names = new[] { project.DisplayName, project.Name }.Concat(module?.Aliases ?? []).Concat(project.Keywords.Where(k => k.Length is >= 2 and <= 12))
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => ArchitectureSearch.Normalize(n!)).Where(n => n.Length >= 2).Distinct().ToList();
        return (await repository.GetQuestionsAsync(null, cancellationToken))
            .Where(q => q.SuggestedProject == project.Key || q.AnsweredProject == project.Key
                        || names.Any(n => System.Text.RegularExpressions.Regex.IsMatch(ArchitectureSearch.Normalize(q.Text), $@"(^|\W){System.Text.RegularExpressions.Regex.Escape(n)}(\W|$)")))
            .OrderByDescending(q => q.Times).Take(80)
            .Select(q => new ReverseModuleQuestion { Id = q.Id, Text = q.Text, Coverage = q.Coverage, Times = q.Times, Status = q.Status }).ToList();
    }

    private static Dictionary<string, string> OtherDocIds(IReadOnlyList<ReverseIndexEntry> entries, string moduleKey, string docType) =>
        entries.Where(e => e.ModuleKey == moduleKey && e.DocType != docType && !e.Removed && e.Kind != "GAP")
            .GroupBy(e => e.ItemId).ToDictionary(g => g.Key, g => g.First().DocType);

    /// <summary>
    /// Itens dos módulos ligados a este (dependências e quem o usa): integrações, endpoints, eventos e tabelas — e os itens
    /// de outros módulos que citam este. Texto enxuto: uma linha por item.
    /// </summary>
    private static string RenderRelated(ArchitectureProject project, List<ArchitectureProject> all, IReadOnlyList<ReverseIndexEntry> entries)
    {
        var related = project.Relations.Select(r => r.Target)
            .Concat(all.Where(p => p.Relations.Any(r => r.Target == project.Key)).Select(p => p.Key))
            .Where(k => k != project.Key).Distinct().ToHashSet();
        var sb = new StringBuilder();
        var lines = 0;
        foreach (var e in entries.Where(e => !e.Removed && related.Contains(e.ModuleKey) && e.Kind is "INT" or "API" or "EVT" or "DB")
                     .OrderBy(e => e.ModuleKey).ThenBy(e => e.Kind).ThenBy(e => e.ItemId))
        {
            if (lines++ >= 250) { sb.AppendLine("… (mais itens: prmake_base_search)"); break; }
            sb.AppendLine($"- {e.Ref} [{e.Kind}] {e.Title}{(e.Tables.Count > 0 ? " · " + string.Join(", ", e.Tables.Take(4)) : "")}");
        }
        var citing = entries.Where(e => !e.Removed && e.ModuleKey != project.Key
                                        && (e.Modules.Contains(project.Key) || e.Refs.Any(r => r.StartsWith(project.Key + "#", StringComparison.Ordinal)))).ToList();
        if (citing.Count > 0)
        {
            sb.AppendLine().AppendLine($"Itens de outros módulos que citam {project.Key}:");
            foreach (var e in citing.Take(120)) sb.AppendLine($"- {e.Ref} [{e.Kind}] {e.Title}");
        }
        return sb.Length == 0 ? "(nenhum módulo relacionado com engenharia reversa publicada ainda — use as relações da ficha do projeto)" : sb.ToString();
    }

    public async Task<List<ReverseRevisionHead>> ListRevisionsAsync(string? moduleKey, string? docType, string? status, CancellationToken cancellationToken)
    {
        var statuses = status switch
        {
            null or "" or "all" => null,
            "open" => OpenStatuses,
            "pending" => new[] { ReverseRevisionStatus.Review, ReverseRevisionStatus.Approved },
            _ => status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };
        var heads = WithProgress(await repository.GetRevisionHeadsAsync(
            string.IsNullOrWhiteSpace(moduleKey) ? null : ArchitectureProject.NormalizeKey(moduleKey),
            string.IsNullOrWhiteSpace(docType) ? null : ReverseDocTypes.Get(docType).Key, statuses, cancellationToken));
        var names = await repository.GetProjectNamesAsync(cancellationToken);
        foreach (var h in heads) h.ModuleName = names.GetValueOrDefault(h.ModuleKey);
        return heads;
    }

    public async Task<ReverseRevisionResponse> GetRevisionAsync(Guid id, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var revision = await repository.GetRevisionAsync(id, tracked: false, cancellationToken) ?? throw new KnowledgeNotFoundException("Revisão não encontrada.");
        // 0070: o projeto sem o texto; o publicado é lido só para calcular o diff (em cache pela versão de cada lado)
        var project = await repository.GetProjectHeadAsync(revision.ModuleKey, cancellationToken) ?? throw NotFound(revision.ModuleKey);
        return await ToRevisionAsync(revision, project, s, userRoles, withDiff: true, cancellationToken);
    }

    public async Task<string> GetRevisionContentAsync(Guid id, CancellationToken cancellationToken) =>
        (await repository.GetRevisionAsync(id, tracked: false, cancellationToken) ?? throw new KnowledgeNotFoundException("Revisão não encontrada.")).Content;

    public async Task<ReverseRevisionResponse> SaveRevisionAsync(Guid id, SaveReverseRevisionRequest request, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var revision = await repository.GetRevisionAsync(id, tracked: true, cancellationToken) ?? throw new KnowledgeNotFoundException("Revisão não encontrada.");
        var approver = CanApprove(s, userRoles);
        if (!approver && !string.Equals(revision.CreatedBy, actor, StringComparison.OrdinalIgnoreCase)
                      && revision.Status is not (ReverseRevisionStatus.Draft or ReverseRevisionStatus.Changes))
            throw new DomainException("Só quem abriu a sessão ou um aprovador edita uma revisão enviada.");
        revision.Save(request.Content, request.Summary, Raw(request.Coverage), request.CoverageRatio, Raw(request.Session), approver, actor, DateTimeOffset.UtcNow);
        if (request.SuggestionDecisions is not null)
            revision.SetSuggestionDecisions(JsonSerializer.Serialize(ReverseSuggestionDecision.Normalize(request.SuggestionDecisions), Json));
        var project = await ProjectAsync(revision.ModuleKey, cancellationToken);
        revision.SetLint(JsonSerializer.Serialize(await LintAsync(project, revision.DocType, revision.Content, revision.CoverageRatio, s, cancellationToken), Json));
        await repository.SaveChangesAsync(cancellationToken);
        return await ToRevisionAsync(revision, project, s, userRoles, withDiff: false, cancellationToken);
    }

    /// <summary>Andamento da sessão (etapa, atividade, registro) — quem abriu a sessão ou um aprovador.</summary>
    public async Task<ReverseRevisionHead> ReportProgressAsync(Guid id, ReverseProgressUpdate update, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var revision = await repository.GetRevisionAsync(id, tracked: true, cancellationToken) ?? throw new KnowledgeNotFoundException("Revisão não encontrada.");
        var progress = revision.ReportProgress(update, DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        return new ReverseRevisionHead
        {
            Id = revision.Id, ModuleKey = revision.ModuleKey, DocType = revision.DocType, Number = revision.Number, Mode = revision.Mode, Status = revision.Status,
            CoverageRatio = revision.CoverageRatio, Length = revision.Content.Length, CreatedAt = revision.CreatedAt, CreatedBy = revision.CreatedBy,
            UpdatedAt = revision.UpdatedAt, UpdatedBy = revision.UpdatedBy, Progress = progress, ProgressAt = revision.ProgressAt
        };
    }

    public async Task<ReverseLintResult> LintAsync(string key, LintReverseDocumentRequest request, CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var project = await ProjectAsync(key, cancellationToken);
        return await LintAsync(project, ReverseDocTypes.Get(request.DocType).Key, request.Content, request.CoverageRatio, s, cancellationToken);
    }

    private async Task<ReverseLintResult> LintAsync(ArchitectureProject project, string docType, string content, double? coverage, ReverseSettings s,
        CancellationToken cancellationToken)
    {
        var type = ReverseDocTypes.Get(docType);
        var entries = await ReverseSearch.EntriesAsync(repository, cancellationToken);
        var published = entries.Where(e => e.ModuleKey == project.Key && e.DocType == type.Key).Select(e => e.ItemId).ToList();
        var removed = entries.Where(e => e.ModuleKey == project.Key && e.Removed).Select(e => e.ItemId).ToHashSet();
        // 0066: com os projetos conhecidos, a checagem valida os destinos e o mecanismo das integrações
        var targets = ReverseRelations.Targets(await repository.GetProjectHeadsAsync(cancellationToken), await repository.GetReverseModulesAsync(cancellationToken));
        return ReverseLint.Run(type, content, published, OtherDocIds(entries, project.Key, type.Key), coverage, s.MinCoverage, removed, project.Key, targets);
    }

    public async Task<ReverseRevisionResponse> SubmitAsync(Guid id, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var revision = await repository.GetRevisionAsync(id, tracked: true, cancellationToken) ?? throw new KnowledgeNotFoundException("Revisão não encontrada.");
        var project = await ProjectAsync(revision.ModuleKey, cancellationToken);
        var lint = await LintAsync(project, revision.DocType, revision.Content, revision.CoverageRatio, s, cancellationToken);
        revision.SetLint(JsonSerializer.Serialize(lint, Json));
        if (!lint.Ok)
        {
            await repository.SaveChangesAsync(cancellationToken);
            throw new DomainException("O documento não passou na checagem — corrija e envie de novo: " + string.Join(" ", lint.Errors));
        }
        revision.Submit(actor, DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        return await ToRevisionAsync(revision, project, s, userRoles, withDiff: false, cancellationToken);
    }

    public async Task<ReverseRevisionResponse> ReviewAsync(Guid id, ReviewReverseRevisionRequest request, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken)
    {
        var s = await RequireApproverAsync(userRoles, "revisar", cancellationToken);
        var revision = await repository.GetRevisionAsync(id, tracked: true, cancellationToken) ?? throw new KnowledgeNotFoundException("Revisão não encontrada.");
        revision.Review(request.Action, request.Note, actor, DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        var project = await ProjectAsync(revision.ModuleKey, cancellationToken);
        return await ToRevisionAsync(revision, project, s, userRoles, withDiff: false, cancellationToken);
    }

    /// <summary>
    /// Publica a revisão aprovada: grava a seção <c>re-&lt;tipo&gt;</c> (versão nova), reconstrói o índice do documento,
    /// funde as integrações (<c>INT</c> com <c>**Módulos:**</c>) nas relações do projeto e marca a publicada anterior como
    /// substituída.
    /// </summary>
    public async Task<ReverseRevisionResponse> PublishAsync(Guid id, PublishReverseRevisionRequest request, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken)
    {
        var s = await RequireApproverAsync(userRoles, "publicar", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var revision = await repository.GetRevisionAsync(id, tracked: true, cancellationToken) ?? throw new KnowledgeNotFoundException("Revisão não encontrada.");
        if (request.Approve && revision.Status == ReverseRevisionStatus.Review) revision.Review("approve", request.Note, actor, now);
        if (revision.Status != ReverseRevisionStatus.Approved)
            throw new DomainException($"Só revisão aprovada pode ser publicada (a #{revision.Number} está {ReverseRevision.Label(revision.Status)}).");
        var type = ReverseDocTypes.Get(revision.DocType);
        // A estrutura (seções obrigatórias) foi checada no envio; aqui só barra o grave — assim uma revisão enviada antes de
        // o modelo ganhar uma seção nova (0053: glossário, banco de dados) continua publicável.
        // mesma checagem do envio (com os IDs dos outros documentos e os removidos — a visão prática cita os publicados)
        var lint = await LintAsync(await ProjectAsync(revision.ModuleKey, cancellationToken), type.Key, revision.Content, null, s, cancellationToken);
        var blocking = lint.Errors.Where(e => !e.StartsWith("Faltam seções obrigatórias", StringComparison.Ordinal)).ToList();
        if (blocking.Count > 0) throw new DomainException("O documento não passou na checagem: " + string.Join(" ", blocking));

        var project = await repository.GetProjectForUpdateAsync(revision.ModuleKey, cancellationToken);
        if (project is null || project.IsDeleted) throw new KnowledgeNotFoundException($"Projeto '{revision.ModuleKey}' não encontrado.");
        var section = project.Sections.FirstOrDefault(x => x.Key == type.SectionKey);
        if (section is null)
        {
            section = new ArchitectureSection(project.Id, type.SectionKey, type.Audience);
            project.Sections.Add(section);
            repository.AddSection(section);
        }
        else section.SetAudience(type.Audience); // 0054: a visão prática é para pessoas (human)
        var note = $"engenharia reversa: revisão #{revision.Number} ({revision.Mode}) aprovada por {revision.ReviewedBy ?? actor}, publicada por {actor}";
        var version = section.Write(type.Title, revision.Content, type.Order, ArchitectureSource.Skill, note, actor, now);
        if (version is not null) repository.AddVersion(version);
        project.Touch(actor, now);

        foreach (var previous in await repository.GetPublishedRevisionsForUpdateAsync(revision.ModuleKey, revision.DocType, cancellationToken))
            if (previous.Id != revision.Id) previous.Supersede(now);
        revision.MarkPublished(section.Version, actor, now);

        var items = ReverseDocParser.Parse(section.Content);
        await ReplaceIndexAsync(repository, project.Key, type.Key, items, section.Version, now, cancellationToken);
        // 0066: as integrações (INT de qualquer documento) são resolvidas na leitura a partir do índice; as relações re# que a
        // publicação gravava antes (destino em texto livre) saem do projeto quando ele é republicado.
        DropReverseRelations(project);

        // 0053: as sugestões que a sessão aplicou/recusou saem da fila com a revisão que as tratou.
        var resolved = new List<ReverseSuggestionDecisionView>();
        foreach (var decision in Decisions(revision))
        {
            var suggestion = await repository.GetSuggestionAsync(decision.SuggestionId, cancellationToken);
            if (suggestion is null || suggestion.Status != ArchitectureSuggestionStatus.Pending || !ForDocument(suggestion, type)) continue;
            var resolution = decision.Decision == "applied"
                ? $"Aplicada na revisão #{revision.Number} de {type.Title}" + (decision.Items.Count > 0 ? $" ({string.Join(", ", decision.Items)})" : "")
                  + (decision.Note is null ? "" : $": {decision.Note}")
                : $"Recusada na revisão #{revision.Number} de {type.Title}: {decision.Note}";
            suggestion.Resolve(decision.Decision == "applied" ? ArchitectureSuggestionStatus.Applied : ArchitectureSuggestionStatus.Dismissed, resolution, actor, now);
            resolved.Add(View(decision, suggestion));
        }

        // 0053: termos do glossário que ainda não são apelido nem palavra-chave viram sugestão na tela.
        var module = await EnsureModuleAsync(project, actor, cancellationToken);
        var glossary = items.Where(i => i.Kind == "GLO" && !i.Removed).SelectMany(i => new[] { i.Title }.Concat(i.Synonyms)).ToList();
        if (glossary.Count > 0)
        {
            var others = (await ReverseSearch.EntriesAsync(repository, cancellationToken))
                .Where(e => e.ModuleKey == project.Key && e.DocType != type.Key);
            module.SuggestTerms(glossary.Concat(ReverseSynonyms.TermsOf(others, project.Key)), project.Keywords);
        }
        await repository.SaveChangesAsync(cancellationToken);
        ReverseSearch.Invalidate();

        var fresh = await ProjectAsync(project.Key, cancellationToken);
        var response = await ToRevisionAsync(revision, fresh, s, userRoles, withDiff: false, cancellationToken);
        response.ResolvedSuggestions = resolved;
        return response;
    }

    /// <summary>Troca os itens do índice de um documento (usado ao publicar e quando a seção re-* é editada pela Base Solvace).</summary>
    public static async Task ReplaceIndexAsync(IKnowledgeRepository repository, string moduleKey, string docType, List<ReverseItem> items, int sectionVersion,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        repository.RemoveIndexEntries(await repository.GetIndexEntriesForUpdateAsync(moduleKey, docType, cancellationToken));
        foreach (var item in items.GroupBy(i => i.Id).Select(g => g.First()))
            repository.AddIndexEntry(new ReverseIndexEntry(moduleKey, docType, item, sectionVersion, now));
    }

    /// <summary>
    /// 0066: tira do projeto as relações <c>re#</c> gravadas pelas publicações antigas — as integrações da engenharia reversa
    /// agora vêm do índice (<see cref="ReverseRelations"/>), com destino validado e o tipo pelo mecanismo.
    /// </summary>
    public static void DropReverseRelations(ArchitectureProject project)
    {
        if (project.Relations.Any(ReverseRelations.FromReverse))
            project.SetRelations(project.Relations.Where(r => !ReverseRelations.FromReverse(r)).ToList());
    }

    private async Task<ReverseRevisionResponse> ToRevisionAsync(ReverseRevision r, ArchitectureProject project, ReverseSettings s, IReadOnlyCollection<string> userRoles,
        bool withDiff, CancellationToken cancellationToken)
    {
        var type = ReverseDocTypes.Get(r.DocType);
        var section = project.Sections.FirstOrDefault(x => x.Key == type.SectionKey);
        ReverseLintResult? lint = null;
        if (r.Lint is not null)
            try { lint = JsonSerializer.Deserialize<ReverseLintResult>(r.Lint, Json); }
            catch (JsonException) { }
        await Task.CompletedTask;
        return new ReverseRevisionResponse
        {
            Id = r.Id, ModuleKey = r.ModuleKey, ModuleName = project.DisplayName ?? project.Name, DocType = r.DocType, Number = r.Number, Mode = r.Mode,
            Status = r.Status, Summary = r.Summary, CoverageRatio = r.CoverageRatio, Length = r.Content.Length, CreatedAt = r.CreatedAt, CreatedBy = r.CreatedBy,
            UpdatedAt = r.UpdatedAt, UpdatedBy = r.UpdatedBy, SubmittedAt = r.SubmittedAt, SubmittedBy = r.SubmittedBy, ReviewedAt = r.ReviewedAt,
            ReviewedBy = r.ReviewedBy, PublishedAt = r.PublishedAt, PublishedBy = r.PublishedBy, ReviewNote = r.ReviewNote, Content = r.Content, Lint = lint,
            Progress = r.Progress is null ? null : ReverseProgress.Parse(r.Progress, r.ProgressAt ?? r.UpdatedAt), ProgressAt = r.ProgressAt,
            Coverage = Element(r.Coverage), Session = Element(r.Session), BaseVersion = r.BaseVersion, PublishedVersion = r.PublishedVersion,
            CurrentPublishedVersion = section?.Version,
            PublishedChangedSinceBase = r.IsOpen && section is not null && r.BaseVersion != section.Version,
            Diff = withDiff ? await DiffAsync(r, section, cancellationToken) : null,
            CanApprove = CanApprove(s, userRoles),
            SuggestionDecisions = await DecisionViewsAsync(r, cancellationToken)
        };
    }

    /// <summary>
    /// 0054: a sugestão é deste documento — aponta a seção dele, ou nenhuma seção da engenharia reversa (as antigas, da
    /// base de antes). A de outro documento fica para a sessão dele (a visão prática não aplica nem fecha a do funcional).
    /// </summary>
    private static bool ForDocument(ArchitectureSuggestion suggestion, ReverseDocType type) =>
        suggestion.SectionKey == type.SectionKey
        || !(suggestion.SectionKey ?? string.Empty).StartsWith(ReverseDocTypes.SectionPrefix, StringComparison.Ordinal);

    private static List<ReverseSuggestionDecision> Decisions(ReverseRevision r)
    {
        if (string.IsNullOrWhiteSpace(r.SuggestionDecisions)) return [];
        try { return JsonSerializer.Deserialize<List<ReverseSuggestionDecision>>(r.SuggestionDecisions, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    private async Task<List<ReverseSuggestionDecisionView>> DecisionViewsAsync(ReverseRevision r, CancellationToken cancellationToken)
    {
        var decisions = Decisions(r);
        if (decisions.Count == 0) return [];
        var views = new List<ReverseSuggestionDecisionView>();
        foreach (var d in decisions)
            views.Add(View(d, await repository.GetSuggestionAsync(d.SuggestionId, cancellationToken)));
        return views;
    }

    private static ReverseSuggestionDecisionView View(ReverseSuggestionDecision d, ArchitectureSuggestion? s) => new()
    {
        SuggestionId = d.SuggestionId, Decision = d.Decision, Items = d.Items, Note = d.Note, Kind = s?.Kind, SectionKey = s?.SectionKey,
        Content = s is null ? null : s.Content.Length <= 600 ? s.Content : s.Content[..600] + "…", CardNumber = s?.CardNumber, Status = s?.Status
    };

    /// <summary>Diferença por item (ID): o que o revisor precisa ver — itens novos, removidos e alterados.</summary>
    // 0070: o diff de documentos de milhões de caracteres era recalculado a cada leitura da revisão (a tela relê a cada evento)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (string Stamp, ReverseRevisionDiff Diff)> Diffs = new();

    private async Task<ReverseRevisionDiff> DiffAsync(ReverseRevision r, ArchitectureSection? section, CancellationToken cancellationToken)
    {
        var stamp = $"{r.UpdatedAt.UtcTicks}:{r.Content.Length}:{section?.ContentHash}:{section?.Version}";
        if (Diffs.TryGetValue(r.Id, out var cached) && cached.Stamp == stamp) return cached.Diff;
        var published = section is null ? string.Empty
            : (await repository.GetSectionContentsAsync([section.Id], cancellationToken)).GetValueOrDefault(section.Id) ?? string.Empty;
        var diff = Diff(published, r.Content);
        if (Diffs.Count > 200) Diffs.Clear();
        Diffs[r.Id] = (stamp, diff);
        return diff;
    }

    public static ReverseRevisionDiff Diff(string published, string draft)
    {
        var before = ReverseDocParser.Parse(published).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        var after = ReverseDocParser.Parse(draft).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        var diff = new ReverseRevisionDiff();
        foreach (var (id, item) in after)
        {
            if (!before.TryGetValue(id, out var old))
            {
                diff.Added++;
                if (diff.Items.Count < 400) diff.Items.Add(new ReverseItemDiff { Id = id, Title = item.Title, Change = "added", After = Cap(item.Body) });
            }
            else if (item.Removed && !old.Removed)
            {
                diff.Removed++;
                if (diff.Items.Count < 400) diff.Items.Add(new ReverseItemDiff { Id = id, Title = item.Title, Change = "removed", Before = Cap(old.Body), After = Cap(item.Body) });
            }
            else if (Squash(item.Body) != Squash(old.Body))
            {
                diff.Changed++;
                if (diff.Items.Count < 400) diff.Items.Add(new ReverseItemDiff { Id = id, Title = item.Title, Change = "changed", Before = Cap(old.Body), After = Cap(item.Body) });
            }
            else diff.Unchanged++;
        }
        foreach (var (id, old) in before.Where(b => !after.ContainsKey(b.Key)))
        {
            diff.Removed++;
            if (diff.Items.Count < 400) diff.Items.Add(new ReverseItemDiff { Id = id, Title = old.Title, Change = "removed", Before = Cap(old.Body) });
        }
        diff.OtherTextChanged = Squash(ReverseDocParser.OutsideItems(published)) != Squash(ReverseDocParser.OutsideItems(draft));
        diff.Items = diff.Items.OrderBy(i => i.Change switch { "removed" => 0, "changed" => 1, _ => 2 }).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
        return diff;
    }

    private static string Squash(string value) => Spaces().Replace(value ?? string.Empty, " ").Trim();

    private static string Cap(string value) => value.Length <= 4000 ? value : value[..4000] + "\n…(cortado)";

    // ── Anexos de UI/UX ─────────────────────────────────────────────────────────────────────────

    public async Task<ReverseAssetResponse> AddLinkAsync(string key, CreateReverseAssetLinkRequest request, string actor, CancellationToken cancellationToken)
    {
        var project = await ProjectAsync(key, cancellationToken);
        var asset = ReverseAsset.Link(project.Key, request.Kind, request.Title, request.Url, request.Notes, request.Screens, actor, DateTimeOffset.UtcNow);
        repository.AddAsset(asset);
        await repository.SaveChangesAsync(cancellationToken);
        return ToAsset(asset);
    }

    public async Task<ReverseAssetResponse> AddFileAsync(string key, string title, string fileName, string contentType, byte[] data, string? notes,
        IEnumerable<string>? screens, string actor, CancellationToken cancellationToken)
    {
        var project = await ProjectAsync(key, cancellationToken);
        var asset = ReverseAsset.File(project.Key, title, fileName, contentType, data, notes, screens, actor, DateTimeOffset.UtcNow);
        repository.AddAsset(asset);
        await repository.SaveChangesAsync(cancellationToken);
        return ToAsset(asset);
    }

    public async Task<(string FileName, string ContentType, byte[] Data)> GetAssetFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var asset = await repository.GetAssetAsync(id, tracked: false, cancellationToken);
        if (asset?.Data is null) throw new KnowledgeNotFoundException("Arquivo não encontrado.");
        return (asset.FileName ?? "arquivo", asset.ContentType ?? "application/octet-stream", asset.Data);
    }

    public async Task DeleteAssetAsync(Guid id, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var asset = await repository.GetAssetAsync(id, tracked: true, cancellationToken) ?? throw new KnowledgeNotFoundException("Anexo não encontrado.");
        if (!CanApprove(s, userRoles) && !string.Equals(asset.CreatedBy, actor, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Só quem anexou ou um aprovador remove o anexo.");
        asset.Delete();
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static ReverseAssetResponse ToAsset(ReverseAsset a) => new()
    {
        Id = a.Id, ModuleKey = a.ModuleKey, Kind = a.Kind, Title = a.Title, Url = a.Url, FileName = a.FileName, ContentType = a.ContentType, Size = a.Size,
        Notes = a.Notes, Screens = a.Screens, CreatedAt = a.CreatedAt, CreatedBy = a.CreatedBy,
        Download = a.FileName is null ? null : $"ReverseEngineering/assets/{a.Id}/file"
    };

    // ── Índice por item ─────────────────────────────────────────────────────────────────────────

    public async Task<List<ReverseIndexHit>> SearchAsync(string? query, IReadOnlyCollection<string>? modules, IReadOnlyCollection<string>? kinds, string? docType,
        int limit, bool includeRemoved, CancellationToken cancellationToken)
    {
        var names = await ModuleNamesAsync(cancellationToken);
        var normalizedModules = modules?.Where(m => !string.IsNullOrWhiteSpace(m)).Select(ArchitectureProject.NormalizeKey).ToList();
        var normalizedKinds = kinds?.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim().ToUpperInvariant()).ToList();
        var doc = string.IsNullOrWhiteSpace(docType) ? null : ReverseDocTypes.Get(docType).Key;
        if (string.IsNullOrWhiteSpace(query))
        {
            // Sem termos: lista o índice filtrado (ex.: todas as regras de um módulo).
            var entries = await ReverseSearch.EntriesAsync(repository, cancellationToken);
            return entries.Where(e => (normalizedModules is not { Count: > 0 } || normalizedModules.Contains(e.ModuleKey))
                                      && (normalizedKinds is not { Count: > 0 } || normalizedKinds.Contains(e.Kind))
                                      && (doc is null || e.DocType == doc) && (includeRemoved || !e.Removed))
                .Take(Math.Clamp(limit, 1, 2000)).Select(e => ReverseSearch.ToHit(e, names, 0, ReverseSearch.Snippet(e.Body))).ToList();
        }
        return await ReverseSearch.RunAsync(repository, query, normalizedModules, normalizedKinds, doc, limit, includeRemoved, names, cancellationToken);
    }

    public async Task<List<ReverseItemResponse>> GetItemsAsync(IReadOnlyCollection<string> refs, string? defaultModule, CancellationToken cancellationToken)
    {
        var entries = await ReverseSearch.EntriesAsync(repository, cancellationToken);
        var names = await ModuleNamesAsync(cancellationToken);
        var module = string.IsNullOrWhiteSpace(defaultModule) ? null : ArchitectureProject.NormalizeKey(defaultModule);
        var result = new List<ReverseItemResponse>();
        var bodies = new Dictionary<Guid, string>();
        foreach (var raw in refs.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().Take(20))
        {
            var parsed = ReverseItemKinds.ParseRef(raw) ?? throw new DomainException($"Referência inválida: '{raw}' (use <módulo>#RN-012).");
            var target = parsed.Module ?? module;
            var matches = entries.Where(e => e.ItemId == parsed.Id && (target is null || e.ModuleKey == target)).ToList();
            if (matches.Count == 0) throw new KnowledgeNotFoundException($"Item {(target is null ? "" : target + "#")}{parsed.Id} não encontrado no índice publicado.");
            if (matches.Count > 1 && target is null)
                throw new DomainException($"{parsed.Id} existe em {matches.Count} módulos ({string.Join(", ", matches.Select(m => m.ModuleKey).Take(8))}) — use <módulo>#{parsed.Id}.");
            var e = matches[0];
            var hit = ReverseSearch.ToHit(e, names, 0, ReverseSearch.Snippet(e.Body));
            // 0070: o cache da busca guarda só o começo do texto — o texto inteiro do item vem do banco
            if (!bodies.ContainsKey(e.Id))
                foreach (var (id, body) in await repository.GetIndexBodiesAsync([e.Id], cancellationToken)) bodies[id] = body;
            result.Add(new ReverseItemResponse
            {
                Ref = hit.Ref, ModuleKey = hit.ModuleKey, ModuleName = hit.ModuleName, DocType = hit.DocType, ItemId = hit.ItemId, Kind = hit.Kind,
                KindLabel = hit.KindLabel, Title = hit.Title, Snippet = hit.Snippet, Tags = hit.Tags, Tables = hit.Tables, Modules = hit.Modules,
                Removed = hit.Removed, Body = bodies.GetValueOrDefault(e.Id) ?? e.Body, Refs = e.Refs, Evidence = e.Evidence, SectionVersion = e.SectionVersion,
                ReferencedBy = entries.Where(x => x.Id != e.Id && (x.Refs.Contains(e.Ref) || (x.ModuleKey == e.ModuleKey && x.Refs.Contains(e.ItemId))))
                    .Select(x => x.Ref).Take(40).ToList(),
                Traps = (await repository.GetTrapsAsync(e.ModuleKey, cancellationToken))
                    .Where(t => t.Items.Contains(e.ItemId) || t.Items.Contains(e.Ref)).Select(ReverseTrapResponse.From).ToList()
            });
        }
        return result;
    }

    /// <summary>
    /// Impacto entre módulos (item 9 da spec): itens de qualquer módulo que citam uma tabela (TB_…), um item
    /// (<c>módulo#ID</c>), um módulo (chave) ou um termo nas tags.
    /// </summary>
    public async Task<List<ReverseIndexHit>> ImpactAsync(string term, int limit, CancellationToken cancellationToken)
    {
        var value = (term ?? string.Empty).Trim();
        if (value.Length < 2) throw new DomainException("Informe uma tabela (TB_…), um item (módulo#RN-012), um módulo ou um termo.");
        var entries = await ReverseSearch.EntriesAsync(repository, cancellationToken);
        var names = await ModuleNamesAsync(cancellationToken);
        var parsed = ReverseItemKinds.ParseRef(value);
        var lower = value.ToLowerInvariant();
        var upper = value.ToUpperInvariant();
        IEnumerable<ReverseIndexEntry> hits = parsed is { } p
            ? entries.Where(e => e.Refs.Contains(p.Module is null ? p.Id : $"{p.Module}#{p.Id}")
                                 || (p.Module is not null && e.ModuleKey == p.Module && e.Refs.Contains(p.Id)))
            : entries.Where(e => e.Tables.Contains(upper) || e.Modules.Contains(lower)
                                 || e.Refs.Any(r => r.StartsWith(lower + "#", StringComparison.Ordinal))
                                 || e.Tags.Any(t => t.Equals(value, StringComparison.OrdinalIgnoreCase)));
        return hits.Where(e => !e.Removed).OrderBy(e => e.ModuleKey).ThenBy(e => e.Kind).ThenBy(e => e.ItemId)
            .Take(Math.Clamp(limit, 1, 500)).Select(e => ReverseSearch.ToHit(e, names, 0, ReverseSearch.Snippet(e.Body))).ToList();
    }

    private Task<Dictionary<string, string>> ModuleNamesAsync(CancellationToken cancellationToken) => repository.GetProjectNamesAsync(cancellationToken);

    // ── Card (analisar-bug) ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pacote compacto para o <c>contexto</c> da analisar-bug: os módulos do card (pelo campo Module, apelidos e área), o
    /// estado da engenharia reversa e os itens que casam com o título/repro — com o texto dos primeiros. Registra o card
    /// (a trava da investigação usa).
    /// </summary>
    public async Task<string> ForCardAsync(string card, string? moduleField, string? query, CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var all = await repository.GetProjectHeadsAsync(cancellationToken);
        var reverseModules = await repository.GetReverseModulesAsync(cancellationToken);
        var entries = await ReverseSearch.EntriesAsync(repository, cancellationToken);
        var names = all.ToDictionary(p => p.Key, p => p.DisplayName ?? p.Name);
        var matched = MatchModules(moduleField, all, reverseModules);

        var withRe = entries.Where(e => !e.Removed).Select(e => e.ModuleKey).ToHashSet();
        if (matched.Count == 0 && !string.IsNullOrWhiteSpace(query))
            matched = (await ReverseSearch.RunAsync(repository, query, null, null, null, 30, false, names, cancellationToken))
                .GroupBy(h => h.ModuleKey).OrderByDescending(g => g.Sum(h => h.Score)).Take(2).Select(g => g.Key).ToList();
        else if (matched.Count > 0)
            matched = await RankForCardAsync(matched, moduleField, query, all, reverseModules, withRe, names, cancellationToken);

        // 0054: "completa" para a análise = os documentos técnicos exigidos (a visão prática é para pessoas)
        var technical = s.TechnicalRequired;
        bool IsComplete(ArchitectureProject p) => technical.Count > 0
                                                  && technical.All(d => ReverseDocTypes.ByKey.TryGetValue(d, out var t) && p.Sections.Any(x => x.Key == t.SectionKey));
        var projects = matched.Select(k => all.FirstOrDefault(p => p.Key == k)).Where(p => p is not null).Select(p => p!).ToList();
        var complete = projects.Any(IsComplete);

        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(card))
        {
            var ctx = await repository.GetCardContextAsync(card.Trim(), tracked: true, cancellationToken);
            if (ctx is null)
            {
                ctx = new ReverseCardContext(card, now);
                repository.AddCardContext(ctx);
            }
            ctx.SetModules(projects.Select(p => p.Key), complete, now);
            await repository.SaveChangesAsync(cancellationToken);
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== ENGENHARIA REVERSA (Base Solvace) — consulte ANTES do código");
        if (projects.Count == 0)
        {
            sb.AppendLine($"Nenhum módulo da engenharia reversa casou com o card (Module: '{moduleField}'). Ache pelo índice: prmake_base_search(\"<tela/termo>\") — ou kb.sh index <termos>.");
            return sb.ToString();
        }
        foreach (var p in projects)
        {
            var published = ReverseDocTypes.All.Where(t => p.Sections.Any(x => x.Key == t.SectionKey))
                .Select(t => $"{t.Key} v{p.Sections.First(x => x.Key == t.SectionKey).Version}").ToList();
            var state = IsComplete(p) ? "COMPLETA" : published.Count > 0 ? "parcial" : "não publicada";
            sb.AppendLine($"Módulo: {p.Key} ({names[p.Key]}) — engenharia reversa {state}" + (published.Count > 0 ? $": {string.Join(", ", published)}" : "")
                          + (withRe.Contains(p.Key) ? $" · {entries.Count(e => e.ModuleKey == p.Key && !e.Removed)} itens" : ""));
            var pair = all.Where(x => x.Key != p.Key && x.BusinessArea is not null && x.BusinessArea == p.BusinessArea && ModuleKinds.Contains(x.Kind)).Select(x => x.Key).ToList();
            if (pair.Count > 0) sb.AppendLine($"  par/mesma área: {string.Join(", ", pair)}");
            var superseded = ReverseSupersession.Compute(p, s, reverseModules.FirstOrDefault(m => m.Key == p.Key));
            if (superseded.Count > 0) sb.AppendLine($"  fonte: engenharia reversa (seções antigas substituídas: {string.Join(", ", superseded.Keys)})");
        }

        var moduleKeys = projects.Select(p => p.Key).Where(withRe.Contains).ToList();
        if (moduleKeys.Count == 0)
        {
            sb.AppendLine("Sem itens publicados para esses módulos nem para os pares da mesma área: procure no índice inteiro ANTES do código — prmake_base_search(\"<tela/termo em PT e EN>\", card) — e só então a base antiga (kb.sh show <projeto> modulos); registre as lacunas (arch.sh suggest <projeto> re-funcional lacuna.md --kind gap --card <card>).");
            return sb.ToString();
        }
        var hits = string.IsNullOrWhiteSpace(query)
            ? []
            : (await ReverseSearch.RunAsync(repository, query, moduleKeys, null, null, 14, false, names, cancellationToken))
                .Where(h => h.DocType != ReverseDocTypes.Practical).Take(10).ToList();
        if (hits.Count == 0)
        {
            sb.AppendLine("Nenhum item casou com o título/repro — procure pelo assunto: prmake_base_search(query, module) (regras: kinds=RN; telas: TELA; casos de uso: UC).");
        }
        else
        {
            sb.AppendLine("Itens que casam com o card (texto inteiro: prmake_base_get(refs, card)):");
            foreach (var h in hits) sb.AppendLine($"- {h.Ref} [{h.KindLabel}] {h.Title}{(h.Tables.Count > 0 ? " · " + string.Join(", ", h.Tables.Take(3)) : "")}");
            // 0054: o que a operação já viu dar errado nesses itens
            var traps = (await repository.GetTrapsAsync(null, cancellationToken))
                .Where(t => hits.Any(h => h.ModuleKey == t.ModuleKey && (t.Items.Contains(h.ItemId) || t.Items.Contains(h.Ref)))).Take(8).ToList();
            if (traps.Count > 0)
            {
                sb.AppendLine("Armadilhas ligadas a esses itens (o que já deu errado):");
                foreach (var t in traps) sb.AppendLine($"- [{string.Join(", ", t.Items)}] {t.Title}{(t.NeedsReview ? " (a conferir)" : "")}{(t.Cards.Count > 0 ? " · cards " + string.Join(", ", t.Cards.Take(4)) : "")}");
            }
            var budget = 5000;
            sb.AppendLine("Texto dos primeiros:");
            foreach (var h in hits.Take(3))
            {
                var body = entries.First(e => e.Ref == h.Ref).Body;
                if (body.Length > 1800) body = body[..1800] + "\n…(prmake_base_get para o resto)";
                if (budget - body.Length < 0) break;
                budget -= body.Length;
                sb.AppendLine($"--- {h.Ref}").AppendLine(body);
            }
        }
        sb.AppendLine(complete
            ? $"REGRA: módulo com engenharia reversa completa — a investigação parte destes itens; código só para confirmar o 'Onde:' citado. Cite os IDs (RN-…/UC-…) no advance de '{GateSteps(s.GateStep).FirstOrDefault() ?? "consultar-base"}' (ou 'lacuna: <o que faltou>' e a sugestão na seção re-*)."
            : "Use os itens acima antes do código; o que faltar → lacuna (arch.sh suggest <projeto> re-funcional lacuna.md --kind gap --card <card>).");
        return sb.ToString();
    }

    /// <summary>
    /// 0060: ordena os módulos do card para a análise. Bug do card 75294: Module "Checklist" casou com 4 módulos revamp
    /// (o corte em 4 com revamp primeiro) e o <c>legado-checklist</c> — o único com engenharia reversa publicada, e o da
    /// tela do card — ficou de fora; o contexto mandou usar a base antiga e a trava da investigação desligou. Agora:
    /// os pares da mesma área com engenharia reversa entram como candidatos (se o campo não escolheu o mundo), o
    /// título/repro decide quem vem primeiro (soma do placar da busca por módulo), depois quem tem engenharia reversa,
    /// depois a ordem do campo. Módulo com itens que casam com o card nunca é cortado.
    /// </summary>
    private async Task<List<string>> RankForCardAsync(List<string> matched, string? moduleField, string? query, List<ArchitectureProject> all,
        List<ReverseModule> reverseModules, HashSet<string> withRe, Dictionary<string, string> names, CancellationToken cancellationToken)
    {
        // 0060b: quem casa direto com o campo (nome/sufixo/apelido) vem antes dos pares da área — no 75294 o legado-centerline
        // (relatórios espelhados, mais acertos somados) passou na frente do legado-checklist.
        var direct = MatchModules(moduleField, all, reverseModules, expandArea: false).ToHashSet();
        var (wantRevamp, wantLegacy) = FieldWorld(moduleField);
        var areas = matched.Select(k => all.FirstOrDefault(p => p.Key == k)?.BusinessArea).Where(a => a is not null).ToHashSet();
        var candidates = matched.Concat(all
                .Where(p => ModuleKinds.Contains(p.Kind) && withRe.Contains(p.Key) && p.BusinessArea is not null && areas.Contains(p.BusinessArea))
                .Where(p => !(wantRevamp && p.Kind == "legacy") && !(wantLegacy && p.Kind != "legacy"))
                .Select(p => p.Key))
            .Distinct().ToList();
        var score = new Dictionary<string, double>();
        var searchable = candidates.Where(withRe.Contains).ToList();
        if (!string.IsNullOrWhiteSpace(query) && searchable.Count > 0)
            foreach (var g in (await ReverseSearch.RunAsync(repository, query, searchable, null, null, 40, false, names, cancellationToken))
                         .Where(h => h.DocType != ReverseDocTypes.Practical).GroupBy(h => h.ModuleKey))
                score[g.Key] = g.Sum(h => (double)h.Score);
        var ranked = candidates
            .Where(k => matched.Contains(k) || score.ContainsKey(k))  // par só entra se casou com o card
            .OrderByDescending(k => direct.Contains(k) && score.ContainsKey(k))
            .ThenByDescending(k => score.GetValueOrDefault(k))
            .ThenByDescending(k => withRe.Contains(k))
            .ThenBy(k => matched.IndexOf(k) is var i && i >= 0 ? i : int.MaxValue)
            .ToList();
        var keep = ranked.Where(score.ContainsKey).ToList();
        return keep.Concat(ranked.Where(k => !keep.Contains(k)).Take(Math.Max(0, 4 - keep.Count))).ToList();
    }

    /// <summary>O campo Module escolhe o mundo? ("Kaizen (Revamp)", "Checklist legado").</summary>
    public static (bool Revamp, bool Legacy) FieldWorld(string? moduleField)
    {
        var n = ArchitectureSearch.Normalize(moduleField ?? string.Empty);
        return (n.Contains("revamp") || n.Contains("novo") || n.Contains("new"), n.Contains("legad") || n.Contains("legacy") || n.Contains("antigo"));
    }

    /// <summary>Módulos do card: apelido exato; senão nome/área/sufixo da chave; "(Revamp)"/"(Legado)" escolhem o mundo.</summary>
    public static List<string> MatchModules(string? moduleField, List<ArchitectureProject> all, List<ReverseModule> modules, bool expandArea = true)
    {
        if (string.IsNullOrWhiteSpace(moduleField)) return [];
        var field = moduleField.Trim();
        var byAlias = modules.Where(m => m.Aliases.Any(a => a.Equals(field, StringComparison.OrdinalIgnoreCase))).Select(m => m.Key).ToList();
        if (byAlias.Count > 0) return byAlias;

        var normalized = ArchitectureSearch.Normalize(field);
        var (wantRevamp, wantLegacy) = FieldWorld(field);
        var core = Spaces().Replace(WorldWords().Replace(normalized, " "), " ").Trim();
        if (core.Length < 2) return [];
        var candidates = all.Where(p => ModuleKinds.Contains(p.Kind)).ToList();
        string Core(string? v) => Spaces().Replace(WorldWords().Replace(ArchitectureSearch.Normalize(v ?? ""), " "), " ").Trim();
        // 0060: nome, área, sufixo da chave ou apelido sem o mundo ("Checklist (legado)" → "checklist") = casamento forte;
        // palavra-chave igual só vale quando não há forte (antes trazia Plano de Ação/RCA para o card de Checklist).
        var aliasKeys = modules.Where(m => m.Aliases.Any(a => Core(a) == core)).Select(m => m.Key).ToHashSet();
        var direct = candidates.Where(p =>
            Core(p.DisplayName) == core
            || Core(p.BusinessArea) == core
            || p.Key.EndsWith("-" + core.Replace(' ', '-'), StringComparison.Ordinal)
            || aliasKeys.Contains(p.Key)).ToList();
        if (direct.Count == 0) direct = candidates.Where(p => p.Keywords.Any(k => Core(k) == core)).ToList();
        var areas = expandArea ? direct.Select(p => p.BusinessArea).Where(a => a is not null).ToHashSet() : new HashSet<string?>();
        var result = direct.Concat(candidates.Where(p => p.BusinessArea is not null && areas.Contains(p.BusinessArea))).Distinct().ToList();
        if (wantRevamp && result.Any(p => p.Kind == "revamp")) result = result.Where(p => p.Kind is "revamp" or "frontend").ToList();
        else if (wantLegacy && result.Any(p => p.Kind == "legacy")) result = result.Where(p => p.Kind == "legacy").ToList();
        // forte primeiro (sem preferir mundo: o título/repro decide no RankForCardAsync), depois os da mesma área
        return result.OrderBy(p => direct.Contains(p) ? 0 : 1).ThenBy(p => p.Kind == "revamp" ? 0 : 1).Select(p => p.Key).Take(6).ToList();
    }

    public async Task RecordConsultedAsync(string card, IEnumerable<string> refs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(card)) return;
        var now = DateTimeOffset.UtcNow;
        var ctx = await repository.GetCardContextAsync(card.Trim(), tracked: true, cancellationToken);
        if (ctx is null)
        {
            ctx = new ReverseCardContext(card, now);
            repository.AddCardContext(ctx);
        }
        ctx.Consulted(refs, now);
        await repository.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Trava da investigação (0052): card de módulo com engenharia reversa completa só conclui a etapa configurada depois
    /// de consultar a base (MCP/REST com o card) ou citando um item/lacuna na mensagem. null = pode seguir.
    /// </summary>
    public async Task<string?> CheckGateAsync(string card, string stepKey, string? message, CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        // 0063: a trava aceita uma lista ("consultar-base,investigar-codigo") — planos novos travam na consulta à base,
        // os antigos (sem essa etapa) continuam travando na investigação.
        if (!GateSteps(s.GateStep).Contains((stepKey ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)) return null;
        var ctx = await repository.GetCardContextAsync((card ?? string.Empty).Trim(), tracked: false, cancellationToken);
        if (ctx is null || !ctx.Complete || ctx.ConsultedAt is not null) return null;
        if (!string.IsNullOrWhiteSpace(message))
        {
            var cited = CitedRefs().Matches(message).Select(m => m.Value).ToList();
            if (cited.Count > 0 || message.Contains("lacuna", StringComparison.OrdinalIgnoreCase))
            {
                await RecordConsultedAsync(card!, cited, cancellationToken);
                return null;
            }
        }
        return $"O card {card} é do módulo {string.Join(", ", ctx.Modules)}, que tem engenharia reversa completa: antes de concluir '{stepKey}', "
               + $"consulte a base (prmake_base_search / prmake_base_get com card={card}) e cite no resumo os itens usados (RN-…, UC-…, API-…), "
               + "ou escreva 'lacuna: <o que a base não cobre>' e registre a sugestão na seção re-* do módulo.";
    }

    /// <summary>Etapas travadas (0063): "consultar-base,investigar-codigo" → as duas; vazio → nenhuma.</summary>
    public static IReadOnlyList<string> GateSteps(string? gate) =>
        (gate ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ── 0054: armadilhas, sugestões por item, lacunas do Pergunte, divergências com o KC ───────

    public async Task<List<ReverseTrapResponse>> ListTrapsAsync(string? moduleKey, CancellationToken cancellationToken) =>
        (await repository.GetTrapsAsync(string.IsNullOrWhiteSpace(moduleKey) ? null : ArchitectureProject.NormalizeKey(moduleKey), cancellationToken))
            .Select(ReverseTrapResponse.From).ToList();

    /// <summary>Cria armadilhas (lote). Quem não aprova — e toda migração automática — cria "a conferir".</summary>
    public async Task<ReverseInfraResponse?> GetInfraAsync(string key, CancellationToken cancellationToken)
    {
        var project = await ProjectAsync(key, cancellationToken);
        var snapshot = await repository.GetInfraAsync(project.Key, cancellationToken);
        return snapshot is null ? null : ReverseInfraResponse.From(snapshot);
    }

    /// <summary>Qualquer usuário logado que rode a skill no módulo grava (como as sessões); o mapa novo substitui o anterior.</summary>
    public async Task<ReverseInfraResponse> UpsertInfraAsync(string key, UpsertReverseInfraRequest request, string actor, CancellationToken cancellationToken)
    {
        var project = await ProjectAsync(key, cancellationToken);
        if (request.Data.ValueKind != System.Text.Json.JsonValueKind.Object) throw new DomainException("Data deve ser um objeto JSON.");
        var json = request.Data.GetRawText();
        var now = DateTimeOffset.UtcNow;
        var existing = await repository.GetInfraForUpdateAsync(project.Key, cancellationToken);
        if (existing is null)
        {
            existing = new ReverseInfraSnapshot(project.Key, request.Account, json, actor, now);
            repository.AddInfra(existing);
        }
        else existing.Replace(request.Account, json, actor, now);
        await repository.SaveChangesAsync(cancellationToken);
        return ReverseInfraResponse.From(existing);
    }

    public async Task<List<ReverseTrapResponse>> CreateTrapsAsync(string key, List<CreateReverseTrapRequest> requests, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var project = await ProjectAsync(key, cancellationToken);
        if (requests.Count is 0 or > 200) throw new DomainException("Envie de 1 a 200 armadilhas por vez.");
        var now = DateTimeOffset.UtcNow;
        var approver = CanApprove(s, userRoles);
        var created = requests.Select(r => new ReverseTrap(project.Key, r.Title, r.Text, r.Items, r.Cards, r.Origin,
            needsReview: !approver || string.Equals(r.Origin, "migrated", StringComparison.OrdinalIgnoreCase), actor, now)).ToList();
        foreach (var t in created) repository.AddTrap(t);
        await repository.SaveChangesAsync(cancellationToken);
        ReverseSearch.Invalidate();
        return created.Select(ReverseTrapResponse.From).ToList();
    }

    public async Task<ReverseTrapResponse> UpdateTrapAsync(Guid id, UpdateReverseTrapRequest request, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        var trap = await repository.GetTrapForUpdateAsync(id, cancellationToken) ?? throw new KnowledgeNotFoundException("Armadilha não encontrada.");
        var approver = CanApprove(s, userRoles);
        if (!approver && !(trap.NeedsReview && string.Equals(trap.CreatedBy, actor, StringComparison.OrdinalIgnoreCase)))
            throw new ReverseForbiddenException($"Só {string.Join(" ou ", s.ApproverRoles)} edita uma armadilha conferida.");
        var now = DateTimeOffset.UtcNow;
        trap.Edit(request.Title, request.Text, request.Items, request.Cards, actor, now);
        if (request.Confirm)
        {
            if (!approver) throw new ReverseForbiddenException($"Só {string.Join(" ou ", s.ApproverRoles)} confere a armadilha.");
            trap.Confirm(actor, now);
        }
        await repository.SaveChangesAsync(cancellationToken);
        return ReverseTrapResponse.From(trap);
    }

    public async Task DeleteTrapAsync(Guid id, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken)
    {
        await RequireApproverAsync(userRoles, "remover armadilhas", cancellationToken);
        var trap = await repository.GetTrapForUpdateAsync(id, cancellationToken) ?? throw new KnowledgeNotFoundException("Armadilha não encontrada.");
        trap.Delete(actor, DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sugestão que não muda o "como o sistema é", mas ensina o que dá errado → armadilha (aprovador, um clique).</summary>
    public async Task<ReverseTrapResponse> SuggestionToTrapAsync(Guid suggestionId, string? title, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken)
    {
        await RequireApproverAsync(userRoles, "converter sugestões em armadilhas", cancellationToken);
        var suggestion = await repository.GetSuggestionAsync(suggestionId, cancellationToken) ?? throw new KnowledgeNotFoundException("Sugestão não encontrada.");
        var now = DateTimeOffset.UtcNow;
        var firstLine = suggestion.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().TrimStart('#', ' ') ?? suggestion.Content;
        var trap = new ReverseTrap(suggestion.ProjectKey, string.IsNullOrWhiteSpace(title) ? (firstLine.Length <= 160 ? firstLine : firstLine[..160] + "…") : title,
            suggestion.Content, suggestion.ItemId is null ? [] : [suggestion.ItemId], suggestion.CardNumber is null ? [] : [suggestion.CardNumber], "suggestion",
            needsReview: false, actor, now);
        trap.Confirm(actor, now);
        repository.AddTrap(trap);
        if (suggestion.Status == ArchitectureSuggestionStatus.Pending)
            suggestion.Resolve(ArchitectureSuggestionStatus.Applied, "Virou armadilha da engenharia reversa", actor, now);
        await repository.SaveChangesAsync(cancellationToken);
        return ReverseTrapResponse.From(trap);
    }

    /// <summary>A skill terminou de ligar as armadilhas antigas e as sugestões aos itens: a seção antiga sai do espelho.</summary>
    public async Task<ReverseModuleResponse> MarkTrapsMigratedAsync(string key, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken)
    {
        var project = await ProjectAsync(key, cancellationToken);
        var tracked = await repository.GetProjectForUpdateAsync(project.Key, cancellationToken) ?? throw new KnowledgeNotFoundException($"Projeto '{key}' não encontrado.");
        var module = await EnsureModuleAsync(tracked, actor, cancellationToken);
        module.MarkTrapsMigrated(actor, DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        return await GetModuleAsync(project.Key, userRoles, cancellationToken);
    }

    /// <summary>Liga uma sugestão a um item/documento da engenharia reversa (migração, análises).</summary>
    public async Task<ArchitectureSuggestionResponse> LinkSuggestionAsync(Guid id, LinkSuggestionItemRequest request, CancellationToken cancellationToken)
    {
        var suggestion = await repository.GetSuggestionAsync(id, cancellationToken) ?? throw new KnowledgeNotFoundException("Sugestão não encontrada.");
        suggestion.LinkItem(request.ItemId, request.SectionKey);
        await repository.SaveChangesAsync(cancellationToken);
        return ToSuggestion(suggestion);
    }

    /// <summary>
    /// Pergunta do "Pergunte" sem resposta (ou parcial) sobre um módulo com engenharia reversa → sugestão na visão prática
    /// (vira FAQ na próxima sessão). Não duplica a mesma pergunta pendente.
    /// </summary>
    public async Task RecordQuestionGapAsync(string projectKey, string question, string actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectKey) || string.IsNullOrWhiteSpace(question)) return;
        var project = (await repository.GetProjectHeadsAsync(cancellationToken)).FirstOrDefault(p => p.Key == ArchitectureProject.NormalizeKey(projectKey));
        if (project is null || !project.Sections.Any(x => x.Key.StartsWith(ReverseDocTypes.SectionPrefix, StringComparison.Ordinal))) return;
        var content = $"Pergunta do Pergunte sem resposta na engenharia reversa: \"{question.Trim()}\"";
        var practical = ReverseDocTypes.ByKey[ReverseDocTypes.Practical].SectionKey;
        if ((await repository.GetSuggestionsAsync(ArchitectureSuggestionStatus.Pending, cancellationToken))
            .Any(x => x.ProjectKey == project.Key && x.Content == content)) return;
        repository.AddSuggestion(new ArchitectureSuggestion(project.Key, practical, "gap", content, null, actor, DateTimeOffset.UtcNow));
        await repository.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Divergências entre o Knowledge Center e o código (sugestões "kc" pendentes) — lista para o time de produto.</summary>
    public async Task<List<ArchitectureSuggestionResponse>> KcDivergencesAsync(CancellationToken cancellationToken) =>
        (await repository.GetSuggestionsAsync(ArchitectureSuggestionStatus.Pending, cancellationToken)).Where(x => x.Kind == "kc").Select(ToSuggestion).ToList();

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<ReverseSettings> RequireApproverAsync(IReadOnlyCollection<string> userRoles, string what, CancellationToken cancellationToken)
    {
        var s = await settingsProvider.GetAsync(cancellationToken);
        if (!CanApprove(s, userRoles))
            throw new ReverseForbiddenException($"Só {string.Join(" ou ", s.ApproverRoles)} pode {what} (configuração ReverseEngineeringApproverRoles).");
        return s;
    }

    /// <summary>Só o projeto pedido (com as seções) — o <c>GetProjectsAsync</c> traz a Base inteira, pesado para um módulo só.</summary>
    private async Task<ArchitectureProject> ProjectAsync(string key, CancellationToken cancellationToken) =>
        await repository.GetProjectAsync(ArchitectureProject.NormalizeKey(key), cancellationToken) ?? throw NotFound(key);

    private static KnowledgeNotFoundException NotFound(string key) =>
        new($"Projeto '{key}' não encontrado na Base Solvace — crie o projeto antes (arch.sh project).");

    private static ArchitectureProject FindProject(List<ArchitectureProject> all, string key)
    {
        var normalized = ArchitectureProject.NormalizeKey(key);
        return all.FirstOrDefault(p => p.Key == normalized)
               ?? throw new KnowledgeNotFoundException($"Projeto '{key}' não encontrado na Base Solvace — crie o projeto antes (arch.sh project).");
    }

    private static string? Raw(JsonElement? element) =>
        element is { } e && e.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ? e.GetRawText() : null;

    private static JsonElement? Element(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    private static ArchitectureSuggestionResponse ToSuggestion(ArchitectureSuggestion s) => new()
    {
        Id = s.Id, ProjectKey = s.ProjectKey, SectionKey = s.SectionKey, Kind = s.Kind, Content = s.Content, CardNumber = s.CardNumber,
        Status = s.Status, CreatedBy = s.CreatedBy, CreatedAt = s.CreatedAt, ResolvedBy = s.ResolvedBy, ResolvedAt = s.ResolvedAt,
        ResolutionNote = s.ResolutionNote, ItemId = s.ItemId
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\b(?:revamp|legado|legacy|novo|antigo|new|old)\b|[()\[\]]")]
    private static partial Regex WorldWords();

    [GeneratedRegex(@"(?:[a-z0-9][a-z0-9._-]*#)?\b(?:TELA|PRF|EST|NTF|CFG|REL|TEC|CMP|API|EVT|JOB|INT|FLX|OBJ|PER|GLO|ADR|NFR|SEQ|GAP|SQL|TRG|TUT|FAQ|FN|UC|RN|DB|UI)-\d{1,4}\b")]
    private static partial Regex CitedRefs();
}

/// <summary>Ação que exige papel de aprovador (403 no REST).</summary>
public class ReverseForbiddenException(string message) : Exception(message);
