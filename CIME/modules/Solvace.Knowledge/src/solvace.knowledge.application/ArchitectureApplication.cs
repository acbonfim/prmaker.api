using System.IO.Compression;
using System.Text;
using System.Text.Json;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Responses;

namespace solvace.knowledge.application;

/// <summary>
/// Engenharia reversa da Solvace (0033): projetos (repositórios e visões transversais) com seções em markdown
/// versionadas. A skill lê primeiro o índice compacto e só depois a seção que precisa; o espelho local
/// (~/.claude/solvace-kb) é este mesmo conteúdo num .zip, baixado só quando o hash muda.
/// O espelho leva só as seções técnicas (público <c>llm</c>); o Guia para pessoas (<c>human</c>, 0038) fica só na tela,
/// sem pesar no índice da análise nem mudar o hash.
/// </summary>
public class ArchitectureApplication(IKnowledgeRepository repository, IKnowledgeSettingsProvider settings) : IArchitectureApplication
{
    public async Task<List<ArchitectureProjectResponse>> ListProjectsAsync(CancellationToken cancellationToken)
    {
        var all = await repository.GetProjectsAsync(cancellationToken);
        return all.OrderBy(p => p.Order).ThenBy(p => p.Name).Select(p => WithUsedBy(ToResponse(p), all)).ToList();
    }

    public async Task<ArchitectureProjectResponse> GetProjectAsync(string key, CancellationToken cancellationToken)
    {
        var all = await repository.GetProjectsAsync(cancellationToken);
        return WithUsedBy(ToResponse(await FindAsync(key, cancellationToken)), all);
    }

    /// <summary>Grafo do ecossistema (0034): arestas agrupadas por (origem, destino, tipo).</summary>
    public async Task<ArchitectureGraphResponse> GetGraphAsync(CancellationToken cancellationToken)
    {
        var projects = await repository.GetProjectsAsync(cancellationToken);
        var graph = new ArchitectureGraphResponse
        {
            Nodes = projects.Select(p => new ArchitectureGraphNode { Key = p.Key, Name = p.Name, Kind = p.Kind, Mapped = true }).ToList()
        };
        var known = projects.Select(p => p.Key).ToHashSet();
        foreach (var group in projects.SelectMany(p => p.Relations.Select(r => (Source: p.Key, Relation: r)))
                     .GroupBy(x => (x.Source, x.Relation.Target, x.Relation.Kind)))
        {
            var target = group.Key.Target;
            if (!known.Contains(target))
            {
                graph.Nodes.Add(new ArchitectureGraphNode { Key = target, Name = ExternalName(target), Kind = target.StartsWith("ext:") ? "external" : "other", Mapped = false });
                known.Add(target);
            }
            graph.Edges.Add(new ArchitectureGraphEdge
            {
                Source = group.Key.Source, Target = target, Kind = group.Key.Kind, Count = group.Count(),
                Details = group.Select(x => x.Relation.Detail ?? string.Empty).Where(d => d.Length > 0).Take(8).ToList()
            });
        }
        return graph;
    }

    private static ArchitectureProjectResponse WithUsedBy(ArchitectureProjectResponse response, List<ArchitectureProject> all)
    {
        response.UsedBy = all.Where(p => p.Key != response.Key)
            .SelectMany(p => p.Relations.Where(r => r.Target == response.Key).Select(r => new ArchitectureIncomingRelation { Source = p.Key, Kind = r.Kind, Detail = r.Detail, Evidence = r.Evidence }))
            .ToList();
        return response;
    }

    /// <summary>Nome legível de um serviço externo (ext:microsoft-graph → Microsoft Graph).</summary>
    public static string ExternalName(string key) => key switch
    {
        "ext:microsoft-graph" => "Microsoft Graph / Teams",
        "ext:azure-ad" => "Azure AD / Entra ID",
        "ext:openai" => "OpenAI / Azure OpenAI",
        "ext:anthropic" => "Anthropic (Claude)",
        "ext:gemini" => "Google Gemini",
        "ext:hubspot" => "HubSpot",
        "ext:azure-devops" => "Azure DevOps",
        "ext:powerbi" => "Power BI",
        "ext:snowflake" => "Snowflake",
        "ext:databricks" => "Databricks",
        "ext:cognito" => "AWS Cognito",
        "ext:s3" => "AWS S3",
        "ext:sqs" => "AWS SQS",
        "ext:opensearch" => "OpenSearch",
        "ext:onlyoffice" => "OnlyOffice",
        _ => key.StartsWith("ext:") ? key[4..] : key
    };

    public async Task<ArchitectureSectionResponse> GetSectionAsync(string projectKey, string sectionKey, CancellationToken cancellationToken)
    {
        var project = await FindAsync(projectKey, cancellationToken);
        return ToSection(FindSection(project, sectionKey), withContent: true);
    }

    public async Task<ArchitectureProjectResponse> UpsertProjectAsync(string key, UpsertArchitectureProjectRequest request, string actor, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var project = await repository.GetProjectForUpdateAsync(ArchitectureProject.NormalizeKey(key), cancellationToken);
        if (project is null)
        {
            project = new ArchitectureProject(key, actor, now);
            repository.AddProject(project);
        }
        project.Update(request.Name, request.Kind, request.Repository, request.Summary, request.Keywords,
            request.SourceCommit, request.SourceBranch, request.Order, actor, now);
        project.SetRelations(request.Relations);
        project.SetFriendly(request.DisplayName, request.Tagline, request.BusinessArea);
        await repository.SaveChangesAsync(cancellationToken);
        return ToResponse(project);
    }

    public async Task DeleteProjectAsync(string key, string actor, CancellationToken cancellationToken)
    {
        var project = await repository.GetProjectForUpdateAsync(ArchitectureProject.NormalizeKey(key), cancellationToken);
        if (project is null || project.IsDeleted)
            throw new KnowledgeNotFoundException($"Projeto '{key}' não encontrado.");
        project.Delete(actor, DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<ArchitectureSectionResponse> WriteSectionAsync(string projectKey, string sectionKey, WriteArchitectureSectionRequest request,
        string actor, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var project = await repository.GetProjectForUpdateAsync(ArchitectureProject.NormalizeKey(projectKey), cancellationToken);
        if (project is null || project.IsDeleted)
            throw new KnowledgeNotFoundException($"Projeto '{projectKey}' não encontrado — crie o projeto antes das seções.");

        var key = ArchitectureProject.NormalizeKey(sectionKey);
        var section = project.Sections.FirstOrDefault(s => s.Key == key);
        if (section is null)
        {
            section = new ArchitectureSection(project.Id, key, request.Audience);
            project.Sections.Add(section);
            repository.AddSection(section);
        }
        else if (section.SetAudience(request.Audience))
            project.Touch(actor, now);
        var version = section.Write(request.Title, request.Content, request.Order ?? (section.Version == 0 ? DefaultOrder(project, section) : null),
            request.Source, request.Note, actor, now);
        if (version is not null)
        {
            repository.AddVersion(version);
            project.Touch(actor, now);
            // 0052: documento da engenharia reversa editado direto na Base Solvace — o índice por item acompanha.
            if (domain.Reverse.ReverseDocTypes.BySection(section.Key) is { } reverseType)
            {
                var items = domain.Reverse.ReverseDocParser.Parse(section.Content);
                await ReverseEngineeringApplication.ReplaceIndexAsync(repository, project.Key, reverseType.Key, items, section.Version, now, cancellationToken);
                if (reverseType.Key == "arquitetura") ReverseEngineeringApplication.MergeIntegrations(project, items);
            }
        }
        await repository.SaveChangesAsync(cancellationToken);
        if (domain.Reverse.ReverseDocTypes.BySection(section.Key) is not null) ReverseSearch.Invalidate();
        return ToSection(section, withContent: true);
    }

    /// <summary>Ordem de uma seção nova sem ordem: técnicas de 10 em 10; o Guia (0038) a partir de 510, depois das técnicas.</summary>
    private static int DefaultOrder(ArchitectureProject project, ArchitectureSection section) =>
        section.IsForLlm
            ? project.Sections.Count(s => s.IsForLlm) * 10
            : project.Sections.Where(s => !s.IsForLlm && s != section).Select(s => s.Order + 10).DefaultIfEmpty(510).Max();

    public async Task<List<ArchitectureSectionVersionResponse>> GetVersionsAsync(string projectKey, string sectionKey, CancellationToken cancellationToken)
    {
        var section = FindSection(await FindAsync(projectKey, cancellationToken), sectionKey);
        return (await repository.GetVersionsAsync(section.Id, cancellationToken))
            .OrderByDescending(v => v.Version).Select(v => ToVersion(v, withContent: false)).ToList();
    }

    public async Task<ArchitectureSectionVersionResponse> GetVersionAsync(string projectKey, string sectionKey, int version, CancellationToken cancellationToken)
    {
        var section = FindSection(await FindAsync(projectKey, cancellationToken), sectionKey);
        var found = await repository.GetVersionAsync(section.Id, version, cancellationToken)
                    ?? throw new KnowledgeNotFoundException($"Versão {version} não encontrada.");
        return ToVersion(found, withContent: true);
    }

    // ── Sugestões (fila do admin) ───────────────────────────────────────────────────────────────

    public async Task<ArchitectureSuggestionResponse> SuggestAsync(CreateArchitectureSuggestionRequest request, string actor, CancellationToken cancellationToken)
    {
        var suggestion = new ArchitectureSuggestion(request.ProjectKey, request.SectionKey, request.Kind, request.Content, request.CardNumber, actor, DateTimeOffset.UtcNow);
        repository.AddSuggestion(suggestion);
        await repository.SaveChangesAsync(cancellationToken);
        return ToSuggestion(suggestion);
    }

    public async Task<List<ArchitectureSuggestionResponse>> GetSuggestionsAsync(string? status, CancellationToken cancellationToken) =>
        (await repository.GetSuggestionsAsync(string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant(), cancellationToken))
            .Select(ToSuggestion).ToList();

    public async Task<ArchitectureSuggestionResponse> ResolveSuggestionAsync(Guid id, ResolveArchitectureSuggestionRequest request, string actor, CancellationToken cancellationToken)
    {
        var suggestion = await repository.GetSuggestionAsync(id, cancellationToken) ?? throw new KnowledgeNotFoundException("Sugestão não encontrada.");
        suggestion.Resolve(request.Status, request.Note, actor, DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        return ToSuggestion(suggestion);
    }

    private static ArchitectureSuggestionResponse ToSuggestion(ArchitectureSuggestion s) => new()
    {
        Id = s.Id, ProjectKey = s.ProjectKey, SectionKey = s.SectionKey, Kind = s.Kind, Content = s.Content, CardNumber = s.CardNumber,
        Status = s.Status, CreatedBy = s.CreatedBy, CreatedAt = s.CreatedAt, ResolvedBy = s.ResolvedBy, ResolvedAt = s.ResolvedAt,
        ResolutionNote = s.ResolutionNote
    };

    // ── Índice e espelho local ──────────────────────────────────────────────────────────────────

    public async Task<string> BuildIndexAsync(CancellationToken cancellationToken)
    {
        var (projects, articles, environment) = await LoadAllAsync(cancellationToken);
        return RenderIndex(projects, articles, environment);
    }

    public async Task<List<ArchitectureSearchHit>> SearchAsync(string query, int limit, IReadOnlyList<string>? extraTerms,
        IReadOnlyCollection<string>? boostProjects, IReadOnlyCollection<string>? boostSections, CancellationToken cancellationToken)
    {
        var terms = ArchitectureSearch.Terms(query, extraTerms);
        if (terms.Count == 0) return [];
        var (projects, articles, _) = await LoadAllAsync(cancellationToken);
        // 0053: sinônimos do glossário da engenharia reversa ("RCA" acha "A3") também na busca/Pergunte da Base Solvace.
        var synonyms = await ReverseSearch.SynonymsAsync(repository, cancellationToken);
        return ArchitectureSearch.Run(projects, articles, terms, Math.Clamp(limit, 1, 50), boostProjects, boostSections, synonyms);
    }

    // ── Perguntas do "Pergunte" (0040) ──────────────────────────────────────────────────────────

    public async Task RecordQuestionAsync(string text, string? kind, string? coverage, string? suggestedProject, string? suggestedSection,
        string actor, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var normalized = ArchitectureQuestion.NormalizeText(text);
        if (normalized.Length < 3) return;
        var question = await repository.GetQuestionByNormalizedAsync(normalized, cancellationToken);
        if (question is null)
        {
            question = new ArchitectureQuestion(text, actor, now);
            repository.AddQuestion(question);
        }
        question.Asked(kind, coverage, suggestedProject, suggestedSection, actor, now);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<ArchitectureQuestionResponse>> GetQuestionsAsync(string? status, bool gapsOnly, CancellationToken cancellationToken) =>
        (await repository.GetQuestionsAsync(string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant(), cancellationToken))
            .Where(q => !gapsOnly || q.IsGap)
            .OrderByDescending(q => q.Times).ThenByDescending(q => q.LastAskedAt)
            .Select(ToQuestion).ToList();

    public async Task<ArchitectureQuestionResponse> ResolveQuestionAsync(Guid id, ResolveArchitectureQuestionRequest request, string actor, CancellationToken cancellationToken)
    {
        var question = await repository.GetQuestionAsync(id, cancellationToken) ?? throw new KnowledgeNotFoundException("Pergunta não encontrada.");
        question.Resolve(request.Status, request.ProjectKey, request.SectionKey, request.Note, actor, DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        return ToQuestion(question);
    }

    private static ArchitectureQuestionResponse ToQuestion(ArchitectureQuestion q) => new()
    {
        Id = q.Id, Text = q.Text, Kind = q.Kind, Coverage = q.Coverage, SuggestedProject = q.SuggestedProject, SuggestedSection = q.SuggestedSection,
        Times = q.Times, FirstAskedAt = q.FirstAskedAt, FirstAskedBy = q.FirstAskedBy, LastAskedAt = q.LastAskedAt, LastAskedBy = q.LastAskedBy,
        Status = q.Status, AnsweredProject = q.AnsweredProject, AnsweredSection = q.AnsweredSection, ResolvedBy = q.ResolvedBy,
        ResolvedAt = q.ResolvedAt, Note = q.Note
    };

    public async Task<string> BuildCatalogAsync(int maxChars, CancellationToken cancellationToken)
    {
        var (projects, articles, _) = await LoadAllAsync(cancellationToken);
        var sb = new StringBuilder();
        foreach (var p in projects)
        {
            sb.Append($"- {p.Key} | {p.Name} ({p.Kind})");
            if (!string.IsNullOrWhiteSpace(p.Summary)) sb.Append($": {(p.Summary.Length > 160 ? p.Summary[..160] + "…" : p.Summary)}");
            if (p.Keywords.Count > 0) sb.Append($" [palavras: {string.Join(", ", p.Keywords.Take(8))}]");
            sb.Append(" — seções: ").AppendLine(string.Join("; ", p.Sections.OrderBy(s => s.Order).Select(s => $"{s.Key} ({s.Title})")));
            if (sb.Length > maxChars) break;
        }
        if (sb.Length < maxChars && articles.Count > 0)
        {
            sb.AppendLine("Knowledge Center (regras de negócio):");
            foreach (var a in articles)
            {
                sb.AppendLine($"- ART-{a.ArticleNumber}: {a.Title}");
                if (sb.Length > maxChars) break;
            }
        }
        return sb.Length <= maxChars ? sb.ToString() : sb.ToString()[..maxChars] + "\n…(cortado)";
    }

    public async Task<ArchitectureExportManifest> GetManifestAsync(CancellationToken cancellationToken)
    {
        var (projects, articles, environment) = await LoadAllAsync(cancellationToken);
        return Manifest(projects, articles, environment);
    }

    public async Task<(ArchitectureExportManifest Manifest, byte[] Zip)> ExportAsync(CancellationToken cancellationToken)
    {
        var (projects, articles, environment) = await LoadAllAsync(cancellationToken);
        var manifest = Manifest(projects, articles, environment);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "INDEX.md", RenderIndex(projects, articles, environment));
            Add(zip, "manifest.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            var usedBy = UsedByMap(projects);
            foreach (var project in projects)
            {
                Add(zip, $"projects/{project.Key}/000-projeto.md", RenderProjectCard(project, usedBy));
                foreach (var section in Exported(project).OrderBy(s => s.Order).ThenBy(s => s.Key))
                    Add(zip, $"projects/{project.Key}/{section.Order:000}-{section.Key}.md", RenderSection(project, section));
            }
            Add(zip, "graph.json", JsonSerializer.Serialize(await GetGraphAsync(cancellationToken), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            // 0052: índice por item da engenharia reversa — a skill acha o item com grep e lê só o bloco dele.
            var reverse = await repository.GetIndexEntriesAsync(null, cancellationToken);
            if (reverse.Count > 0)
            {
                Add(zip, "reverse/INDEX.md", RenderReverseIndex(projects, reverse));
                foreach (var byModule in reverse.GroupBy(e => e.ModuleKey))
                    Add(zip, $"reverse/{byModule.Key}.tsv", RenderReverseTsv(byModule));
            }
            Add(zip, "knowledge/INDEX.md", RenderKnowledgeIndex(articles, environment));
            foreach (var article in articles)
                Add(zip, $"knowledge/ART-{article.ArticleNumber}.md", RenderArticle(article));
        }
        return (manifest, buffer.ToArray());
    }

    private async Task<(List<ArchitectureProject> Projects, List<KnowledgeArticle> Articles, string Environment)> LoadAllAsync(CancellationToken cancellationToken)
    {
        var environment = (await settings.GetAsync(cancellationToken)).ActiveEnvironment;
        var projects = (await repository.GetProjectsAsync(cancellationToken)).OrderBy(p => p.Order).ThenBy(p => p.Name).ToList();
        var articles = (await repository.GetArticlesAsync(environment, tracked: false, cancellationToken)).OrderBy(a => a.ArticleNumber).ToList();
        return (projects, articles, environment);
    }

    private static ArchitectureExportManifest Manifest(List<ArchitectureProject> projects, List<KnowledgeArticle> articles, string environment)
    {
        var fingerprint = new StringBuilder(environment);
        foreach (var p in projects)
        {
            fingerprint.Append('|').Append(p.Key).Append(':').Append(p.Name).Append(':').Append(p.Kind).Append(':').Append(p.Summary)
                .Append(':').Append(string.Join(",", p.Keywords)).Append(':').Append(p.SourceCommit).Append(':').Append(p.Order);
            foreach (var s in Exported(p).OrderBy(s => s.Key)) fingerprint.Append(';').Append(s.Key).Append('=').Append(s.ContentHash).Append('@').Append(s.Order);
            foreach (var r in p.Relations) fingerprint.Append(">").Append(r.Target).Append(':').Append(r.Kind).Append(':').Append(r.Detail);
        }
        foreach (var a in articles) fingerprint.Append("|kc").Append(a.ArticleNumber).Append('=').Append(a.ContentHash);
        return new ArchitectureExportManifest
        {
            Hash = KnowledgeArticle.Hash(fingerprint.ToString())[..16],
            GeneratedAt = DateTimeOffset.UtcNow,
            Projects = projects.Count,
            Sections = projects.Sum(p => Exported(p).Count()),
            KnowledgeEnvironment = environment,
            KnowledgeArticles = articles.Count
        };
    }

    /// <summary>
    /// Índice compacto (lido em todo card): uma linha por projeto — resumo curto, palavras-chave principais, seções e
    /// contagem de integrações — e o mapa das regras de negócio do KC. O resumo completo e as dependências com evidência
    /// ficam em <c>projects/&lt;projeto&gt;/000-projeto.md</c> (0034: com o parque inteiro o índice detalhado passava de 10k tokens).
    /// </summary>
    private static string RenderIndex(List<ArchitectureProject> projects, List<KnowledgeArticle> articles, string environment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Base Solvace — índice");
        sb.AppendLine();
        sb.AppendLine("Leia este índice primeiro; depois abra só o que o caso pede: `projects/<projeto>/000-projeto.md` (resumo, depende de / usado por,");
        sb.AppendLine("com evidência), `projects/<projeto>/<ordem>-<seção>.md`, `knowledge/ART-n.md`. `⇄ d/u` = depende de d projetos, usado por u.");
        if (projects.Count == 0) sb.AppendLine().AppendLine("_Nenhum projeto mapeado ainda._");
        var usedBy = UsedByMap(projects);
        foreach (var group in projects.GroupBy(p => p.Kind))
        {
            sb.AppendLine().AppendLine($"## {KindLabel(group.Key)}");
            foreach (var p in group)
            {
                sb.Append($"- **{p.Name}** `{p.Key}` — {Short(p.Summary, 180)}");
                if (p.Keywords.Count > 0) sb.Append($" · kw: {string.Join(", ", p.Keywords.Take(6))}");
                var exported = Exported(p).ToList();
                if (exported.Count > 0)
                    sb.Append($" · seções {string.Join("·", exported.OrderBy(s => s.Order).ThenBy(s => s.Key).Select(s => $"{s.Order:000}"))}"
                              + $" (~{Math.Max(1, exported.Sum(s => s.Content.Length) / 4 / 100) * 100} tok)");
                var reverseDocs = exported.Count(s => domain.Reverse.ReverseDocTypes.BySection(s.Key) is not null);
                if (reverseDocs > 0) sb.Append($" · **RE {reverseDocs}/{domain.Reverse.ReverseDocTypes.All.Count}** (reverse/{p.Key}.tsv)");
                var deps = p.Relations.Select(r => r.Target).Distinct().Count();
                var users = usedBy.TryGetValue(p.Key, out var u) ? u.Select(x => x.Source).Distinct().Count() : 0;
                if (deps + users > 0) sb.Append($" · ⇄ {deps}/{users}");
                sb.AppendLine();
            }
        }

        sb.AppendLine().AppendLine($"## Regras de negócio — Knowledge Center ({environment}, {articles.Count} artigos)");
        if (articles.Count == 0)
            sb.AppendLine().AppendLine("_Nenhum artigo sincronizado (ou todos filtrados como teste)._");
        foreach (var byCategory in articles.GroupBy(a => a.Category ?? "Sem categoria").OrderBy(g => g.Key))
        {
            sb.AppendLine().AppendLine($"**{byCategory.Key}**");
            foreach (var a in byCategory)
                sb.AppendLine($"- ART-{a.ArticleNumber} {a.Title}{(a.Subcategory is null ? "" : $" · {a.Subcategory}")}{(a.Tags.Count == 0 ? "" : $" · tags: {string.Join(", ", a.Tags)}")}");
        }
        return sb.ToString();
    }

    /// <summary>Engenharia reversa no espelho (0052): módulos com documentos publicados e itens por tipo.</summary>
    private static string RenderReverseIndex(List<ArchitectureProject> projects, List<ReverseIndexEntry> entries)
    {
        var names = projects.ToDictionary(p => p.Key, p => p.DisplayName ?? p.Name);
        var sb = new StringBuilder("# Engenharia reversa — índice por item\n\n");
        sb.AppendLine("Cada módulo tem `reverse/<módulo>.tsv` (ID, tipo, documento, título, tabelas, tags — uma linha por item). Ache com");
        sb.AppendLine("`kb.sh re find <termos>` (ou grep no .tsv) e leia só o bloco: `kb.sh re get <módulo>#<ID>` (ou MCP `prmake_base_get`).");
        sb.AppendLine("Tipos: " + string.Join(" · ", domain.Reverse.ReverseItemKinds.All.Select(k => $"{k.Prefix} {k.Label.ToLowerInvariant()}")));
        sb.AppendLine();
        foreach (var byModule in entries.Where(e => !e.Removed).GroupBy(e => e.ModuleKey).OrderBy(g => g.Key))
        {
            var docs = byModule.Select(e => e.DocType).Distinct().OrderBy(d => d);
            var kinds = byModule.GroupBy(e => e.Kind).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
            sb.AppendLine($"- `{byModule.Key}` {names.GetValueOrDefault(byModule.Key)} — docs: {string.Join(", ", docs)} · {string.Join(" · ", kinds)}");
        }
        return sb.ToString();
    }

    private static string RenderReverseTsv(IEnumerable<ReverseIndexEntry> entries)
    {
        static string Clean(string v) => v.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        var sb = new StringBuilder("id\tkind\tdoc\ttitle\ttables\ttags\tmodules\n");
        foreach (var e in entries.OrderBy(e => e.DocType).ThenBy(e => e.Order))
            sb.Append($"{e.ItemId}\t{e.Kind}\t{e.DocType}\t{Clean(e.Removed ? "(removido) " + e.Title : e.Title)}\t{string.Join(",", e.Tables)}\t{Clean(string.Join(",", e.Tags))}\t{string.Join(",", e.Modules)}\n");
        return sb.ToString();
    }

    /// <summary>Ficha do projeto no espelho local: resumo completo, palavras-chave, seções e relações com evidência.</summary>
    private static string RenderProjectCard(ArchitectureProject p, Dictionary<string, List<(string Source, ArchitectureRelation Relation)>> usedBy)
    {
        var sb = new StringBuilder($"<!-- {p.Key} · ficha gerada pelo PRMake -->\n# {p.Name} (`{p.Key}`)\n\n");
        sb.AppendLine($"Tipo: {KindLabel(p.Kind)}" + (p.Repository is null ? "" : $" · repositório: {p.Repository}")
                      + (p.SourceCommit is null ? "" : $" · commit {p.SourceCommit[..Math.Min(8, p.SourceCommit.Length)]}")
                      + (p.SourceMappedAt is null ? "" : $" ({p.SourceMappedAt:yyyy-MM-dd})"));
        if (p.Summary is not null) sb.AppendLine().AppendLine(p.Summary);
        if (p.Keywords.Count > 0) sb.AppendLine().AppendLine($"Palavras-chave: {string.Join(", ", p.Keywords)}");
        var exported = Exported(p).ToList();
        if (exported.Count > 0)
        {
            sb.AppendLine().AppendLine("## Seções");
            foreach (var s in exported.OrderBy(s => s.Order).ThenBy(s => s.Key))
                sb.AppendLine($"- `{s.Order:000}-{s.Key}.md` {s.Title} (~{Math.Max(1, s.Content.Length / 4 / 100) * 100} tokens)");
        }
        if (p.Relations.Count > 0)
        {
            sb.AppendLine().AppendLine("## Depende de");
            foreach (var r in p.Relations.OrderBy(r => r.Kind).ThenBy(r => r.Target))
                sb.AppendLine($"- `{r.Target}` — {RelationLabel(r.Kind)}: {r.Detail}" + (r.Evidence is null ? "" : $" ({r.Evidence})"));
        }
        if (usedBy.TryGetValue(p.Key, out var incoming) && incoming.Count > 0)
        {
            sb.AppendLine().AppendLine("## Usado por");
            foreach (var (source, r) in incoming.OrderBy(x => x.Relation.Kind).ThenBy(x => x.Source))
                sb.AppendLine($"- `{source}` — {RelationLabel(r.Kind)}: {r.Detail}" + (r.Evidence is null ? "" : $" ({r.Evidence})"));
        }
        return sb.ToString();
    }

    /// <summary>Seções que vão para o espelho/índice das skills: só as técnicas (o Guia é para pessoas — 0038).</summary>
    internal static IEnumerable<ArchitectureSection> Exported(ArchitectureProject project) => project.Sections.Where(s => s.IsForLlm);

    private static Dictionary<string, List<(string Source, ArchitectureRelation Relation)>> UsedByMap(List<ArchitectureProject> projects) =>
        projects.SelectMany(p => p.Relations.Where(r => r.Target != p.Key).Select(r => (Source: p.Key, Relation: r)))
            .GroupBy(x => x.Relation.Target).ToDictionary(g => g.Key, g => g.ToList());

    private static string RelationLabel(string kind) => kind switch
    {
        "event" => "evento (SNS → fila)",
        "queue" => "fila (SQS)",
        "database" => "banco compartilhado",
        "http" => "HTTP",
        "package" => "pacote",
        "external" => "serviço externo",
        "frontend" => "front-end",
        _ => "outro"
    };

    /// <summary>Corta no fim de frase (ou palavra) antes do limite.</summary>
    internal static string Short(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Trim();
        if (text.Length <= max) return text;
        var cut = text[..max];
        var sentence = cut.LastIndexOfAny(['.', '!', '?']);
        if (sentence >= max / 2) return cut[..(sentence + 1)];
        var space = cut.LastIndexOf(' ');
        return (space > 0 ? cut[..space] : cut).TrimEnd(',', ';', ':') + "…";
    }

    private static string RenderSection(ArchitectureProject project, ArchitectureSection section) =>
        $"<!-- {project.Key}/{section.Key} · versão {section.Version} · {section.UpdatedAt:yyyy-MM-dd} por {section.UpdatedBy}"
        + (project.SourceCommit is null ? "" : $" · commit {project.SourceCommit}") + $" -->\n# {project.Name} — {section.Title}\n\n{section.Content}\n";

    private static string RenderKnowledgeIndex(List<KnowledgeArticle> articles, string environment)
    {
        var sb = new StringBuilder($"# Knowledge Center ({environment}) — {articles.Count} artigos\n\n");
        foreach (var a in articles)
            sb.AppendLine($"- ART-{a.ArticleNumber} | {a.Title} | {a.Category}{(a.Subcategory is null ? "" : " / " + a.Subcategory)}{(a.Tags.Count == 0 ? "" : " | " + string.Join(", ", a.Tags))}");
        return sb.ToString();
    }

    private static string RenderArticle(KnowledgeArticle a) =>
        $"# ART-{a.ArticleNumber} — {a.Title}\n\nCategoria: {a.Category}{(a.Subcategory is null ? "" : " / " + a.Subcategory)}"
        + (a.Tags.Count == 0 ? "" : $"\nTags: {string.Join(", ", a.Tags)}")
        + $"\nAtualizado no KC: {a.SourceUpdatedAt:yyyy-MM-dd} · ambiente {a.Environment}\n\n{a.Content}\n";

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string KindLabel(string kind) => kind switch
    {
        "ecosystem" => "Ecossistema",
        "legacy" => "Legado (edv-solvace)",
        "frontend" => "Front-end",
        "integration" => "Integrações",
        "revamp" => "Revamp (módulos)",
        "infra" => "Infraestrutura / AWS",
        "third-party" => "Serviços de terceiros",
        "auth" => "Login e autenticação",
        "business-rules" => "Regras de negócio",
        _ => "Outros"
    };

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<ArchitectureProject> FindAsync(string key, CancellationToken cancellationToken)
    {
        var normalized = ArchitectureProject.NormalizeKey(key);
        return (await repository.GetProjectsAsync(cancellationToken)).FirstOrDefault(p => p.Key == normalized)
               ?? throw new KnowledgeNotFoundException($"Projeto '{key}' não encontrado.");
    }

    private static ArchitectureSection FindSection(ArchitectureProject project, string sectionKey)
    {
        var key = ArchitectureProject.NormalizeKey(sectionKey);
        return project.Sections.FirstOrDefault(s => s.Key == key)
               ?? throw new KnowledgeNotFoundException($"Seção '{sectionKey}' não encontrada em '{project.Key}'.");
    }

    private static ArchitectureProjectResponse ToResponse(ArchitectureProject p) => new()
    {
        Id = p.Id,
        Key = p.Key,
        Name = p.Name,
        Kind = p.Kind,
        Repository = p.Repository,
        Summary = p.Summary,
        Keywords = p.Keywords,
        SourceCommit = p.SourceCommit,
        SourceBranch = p.SourceBranch,
        SourceMappedAt = p.SourceMappedAt,
        Order = p.Order,
        DisplayName = p.DisplayName,
        Tagline = p.Tagline,
        BusinessArea = p.BusinessArea,
        UpdatedAt = p.UpdatedAt,
        UpdatedBy = p.UpdatedBy,
        Sections = p.Sections.OrderBy(s => s.Order).ThenBy(s => s.Key).Select(s => (ArchitectureSectionSummaryResponse)ToSection(s, withContent: false)).ToList(),
        Relations = p.Relations
    };

    private static ArchitectureSectionResponse ToSection(ArchitectureSection s, bool withContent) => new()
    {
        Id = s.Id,
        Key = s.Key,
        Title = s.Title,
        Order = s.Order,
        Version = s.Version,
        Source = s.Source,
        Audience = s.Audience,
        Length = s.Content.Length,
        UpdatedAt = s.UpdatedAt,
        UpdatedBy = s.UpdatedBy,
        Content = withContent ? s.Content : string.Empty
    };

    private static ArchitectureSectionVersionResponse ToVersion(ArchitectureSectionVersion v, bool withContent) => new()
    {
        Version = v.Version,
        Title = v.Title,
        Source = v.Source,
        Note = v.Note,
        CreatedBy = v.CreatedBy,
        CreatedAt = v.CreatedAt,
        Content = withContent ? v.Content : null
    };
}
