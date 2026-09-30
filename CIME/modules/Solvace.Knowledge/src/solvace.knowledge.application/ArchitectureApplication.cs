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
/// </summary>
public class ArchitectureApplication(IKnowledgeRepository repository, IKnowledgeSettingsProvider settings) : IArchitectureApplication
{
    public async Task<List<ArchitectureProjectResponse>> ListProjectsAsync(CancellationToken cancellationToken) =>
        (await repository.GetProjectsAsync(cancellationToken)).OrderBy(p => p.Order).ThenBy(p => p.Name).Select(ToResponse).ToList();

    public async Task<ArchitectureProjectResponse> GetProjectAsync(string key, CancellationToken cancellationToken) =>
        ToResponse(await FindAsync(key, cancellationToken));

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
            section = new ArchitectureSection(project.Id, key);
            project.Sections.Add(section);
            repository.AddSection(section);
        }
        var version = section.Write(request.Title, request.Content, request.Order ?? (section.Version == 0 ? project.Sections.Count * 10 : null),
            request.Source, request.Note, actor, now);
        if (version is not null)
        {
            repository.AddVersion(version);
            project.Touch(actor, now);
        }
        await repository.SaveChangesAsync(cancellationToken);
        return ToSection(section, withContent: true);
    }

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

    // ── Índice e espelho local ──────────────────────────────────────────────────────────────────

    public async Task<string> BuildIndexAsync(CancellationToken cancellationToken)
    {
        var (projects, articles, environment) = await LoadAllAsync(cancellationToken);
        return RenderIndex(projects, articles, environment);
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
            foreach (var project in projects)
            foreach (var section in project.Sections.OrderBy(s => s.Order).ThenBy(s => s.Key))
                Add(zip, $"projects/{project.Key}/{section.Order:000}-{section.Key}.md", RenderSection(project, section));
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
            foreach (var s in p.Sections.OrderBy(s => s.Key)) fingerprint.Append(';').Append(s.Key).Append('=').Append(s.ContentHash).Append('@').Append(s.Order);
        }
        foreach (var a in articles) fingerprint.Append("|kc").Append(a.ArticleNumber).Append('=').Append(a.ContentHash);
        return new ArchitectureExportManifest
        {
            Hash = KnowledgeArticle.Hash(fingerprint.ToString())[..16],
            GeneratedAt = DateTimeOffset.UtcNow,
            Projects = projects.Count,
            Sections = projects.Sum(p => p.Sections.Count),
            KnowledgeEnvironment = environment,
            KnowledgeArticles = articles.Count
        };
    }

    /// <summary>
    /// Índice compacto: por projeto, o resumo, as palavras-chave e as seções (com tamanho aproximado em tokens), e o
    /// mapa das regras de negócio do KC. Pensado para caber em poucos milhares de tokens para o parque inteiro.
    /// </summary>
    private static string RenderIndex(List<ArchitectureProject> projects, List<KnowledgeArticle> articles, string environment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Base Solvace — índice");
        sb.AppendLine();
        sb.AppendLine("Leia este índice primeiro; abra só a seção/artigo que o caso pede (`projects/<projeto>/<ordem>-<seção>.md`, `knowledge/ART-n.md`).");
        if (projects.Count == 0) sb.AppendLine().AppendLine("_Nenhum projeto mapeado ainda._");
        foreach (var group in projects.GroupBy(p => p.Kind))
        {
            sb.AppendLine().AppendLine($"## {KindLabel(group.Key)}");
            foreach (var p in group)
            {
                sb.AppendLine().Append($"### {p.Name} (`{p.Key}`)");
                if (p.SourceCommit is not null) sb.Append($" — commit {p.SourceCommit[..Math.Min(8, p.SourceCommit.Length)]}");
                sb.AppendLine();
                if (p.Repository is not null) sb.AppendLine($"Repositório: {p.Repository}");
                if (p.Summary is not null) sb.AppendLine(p.Summary);
                if (p.Keywords.Count > 0) sb.AppendLine($"Palavras-chave: {string.Join(", ", p.Keywords)}");
                if (p.Sections.Count > 0)
                    sb.AppendLine("Seções: " + string.Join(" · ", p.Sections.OrderBy(s => s.Order).ThenBy(s => s.Key)
                        .Select(s => $"`{s.Order:000}-{s.Key}` {s.Title} (~{Math.Max(1, s.Content.Length / 4 / 100) * 100} tokens)")));
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
        UpdatedAt = p.UpdatedAt,
        UpdatedBy = p.UpdatedBy,
        Sections = p.Sections.OrderBy(s => s.Order).ThenBy(s => s.Key).Select(s => (ArchitectureSectionSummaryResponse)ToSection(s, withContent: false)).ToList()
    };

    private static ArchitectureSectionResponse ToSection(ArchitectureSection s, bool withContent) => new()
    {
        Id = s.Id,
        Key = s.Key,
        Title = s.Title,
        Order = s.Order,
        Version = s.Version,
        Source = s.Source,
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
