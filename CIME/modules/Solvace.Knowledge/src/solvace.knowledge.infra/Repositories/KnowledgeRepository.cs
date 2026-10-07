using Microsoft.EntityFrameworkCore;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Responses;
using solvace.knowledge.infra.Contexts;

namespace solvace.knowledge.infra.Repositories;

public class KnowledgeRepository(KnowledgeContext context) : IKnowledgeRepository
{
    public Task<List<KnowledgeArticle>> GetArticlesAsync(string environment, bool tracked, CancellationToken cancellationToken)
    {
        var query = context.Articles.Where(a => a.Environment == environment);
        return (tracked ? query : query.AsNoTracking()).OrderBy(a => a.ArticleNumber).ToListAsync(cancellationToken);
    }

    public Task<KnowledgeArticle?> GetArticleAsync(string environment, int articleNumber, CancellationToken cancellationToken) =>
        context.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Environment == environment && a.ArticleNumber == articleNumber, cancellationToken);

    public async Task<List<KnowledgeArticle>> SearchArticlesAsync(string environment, string? term, int limit, CancellationToken cancellationToken)
    {
        var articles = await context.Articles.AsNoTracking().Where(a => a.Environment == environment).ToListAsync(cancellationToken);
        var words = (term ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToArray();
        if (words.Length == 0)
            return articles.OrderByDescending(a => a.SourceUpdatedAt).Take(limit).ToList();

        // Poucos artigos (dezenas/centenas): pontua em memória — título e tags valem mais que o texto.
        return articles
            .Select(a => (Article: a, Score: words.Sum(w =>
                (a.Title.Contains(w, StringComparison.OrdinalIgnoreCase) ? 5 : 0)
                + (a.Tags.Any(t => t.Contains(w, StringComparison.OrdinalIgnoreCase)) ? 3 : 0)
                + ((a.Category ?? "").Contains(w, StringComparison.OrdinalIgnoreCase) || (a.Subcategory ?? "").Contains(w, StringComparison.OrdinalIgnoreCase) ? 2 : 0)
                + (a.Content.Contains(w, StringComparison.OrdinalIgnoreCase) ? 1 : 0))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Article.ArticleNumber)
            .Take(limit).Select(x => x.Article).ToList();
    }

    public void AddArticle(KnowledgeArticle article) => context.Articles.Add(article);
    public void RemoveArticle(KnowledgeArticle article) => context.Articles.Remove(article);

    public Task<KnowledgeSyncState?> GetStateAsync(string environment, CancellationToken cancellationToken) =>
        context.SyncStates.FirstOrDefaultAsync(s => s.Environment == environment, cancellationToken);

    public void AddState(KnowledgeSyncState state) => context.SyncStates.Add(state);

    public Task<List<ArchitectureProject>> GetProjectsAsync(CancellationToken cancellationToken) =>
        context.Projects.AsNoTracking().Include(p => p.Sections).Where(p => !p.IsDeleted).OrderBy(p => p.Order).ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<List<ArchitectureProject>> GetProjectHeadsAsync(CancellationToken cancellationToken) =>
        (await SnapshotAsync(cancellationToken)).Heads.Select(p => p.ReadCopy()).ToList();

    private async Task<List<ArchitectureProject>> LoadProjectHeadsAsync(CancellationToken cancellationToken)
    {
        var projects = await context.Projects.AsNoTracking().Where(p => !p.IsDeleted).OrderBy(p => p.Order).ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);
        var ids = projects.Select(p => p.Id).ToList();
        // length() no banco: o texto não sai do Postgres
        var sections = (await context.Sections.AsNoTracking().Where(s => ids.Contains(s.ProjectId))
                .Select(s => new { s.Id, s.ProjectId, s.Key, s.Title, s.Order, s.ContentHash, s.Version, s.Source, s.Audience, s.UpdatedAt, s.UpdatedBy, Length = s.Content.Length })
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.ProjectId);
        foreach (var p in projects)
            p.Sections.AddRange(sections[p.Id].Select(s => ArchitectureSection.Head(s.Id, s.ProjectId, s.Key, s.Title, s.Order, s.ContentHash, s.Version,
                s.Source, s.Audience, s.UpdatedAt, s.UpdatedBy, s.Length)));
        return projects;
    }

    public Task<ArchitectureProject?> GetProjectAsync(string key, CancellationToken cancellationToken) =>
        context.Projects.AsNoTracking().Include(p => p.Sections).FirstOrDefaultAsync(p => p.Key == key && !p.IsDeleted, cancellationToken);

    public Task<Dictionary<string, string>> GetProjectNamesAsync(CancellationToken cancellationToken) =>
        context.Projects.AsNoTracking().Where(p => !p.IsDeleted).Select(p => new { p.Key, Name = p.DisplayName ?? p.Name })
            .ToDictionaryAsync(p => p.Key, p => p.Name, cancellationToken);

    public Task<ArchitectureProject?> GetProjectForUpdateAsync(string key, CancellationToken cancellationToken) =>
        context.Projects.Include(p => p.Sections).FirstOrDefaultAsync(p => p.Key == key, cancellationToken);

    public void AddProject(ArchitectureProject project) => context.Projects.Add(project);
    public void AddSection(ArchitectureSection section) => context.Sections.Add(section);
    public void AddVersion(ArchitectureSectionVersion version) => context.SectionVersions.Add(version);

    public Task<List<ArchitectureSectionVersion>> GetVersionsAsync(Guid sectionId, CancellationToken cancellationToken) =>
        context.SectionVersions.AsNoTracking().Where(v => v.SectionId == sectionId).OrderByDescending(v => v.Version).ToListAsync(cancellationToken);

    public Task<ArchitectureSectionVersion?> GetVersionAsync(Guid sectionId, int version, CancellationToken cancellationToken) =>
        context.SectionVersions.AsNoTracking().FirstOrDefaultAsync(v => v.SectionId == sectionId && v.Version == version, cancellationToken);

    public void AddSuggestion(ArchitectureSuggestion suggestion) => context.Suggestions.Add(suggestion);

    public Task<ArchitectureSuggestion?> GetSuggestionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Suggestions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<List<ArchitectureSuggestion>> GetSuggestionsAsync(string? status, CancellationToken cancellationToken) =>
        context.Suggestions.AsNoTracking().Where(s => status == null || s.Status == status)
            .OrderByDescending(s => s.CreatedAt).Take(500).ToListAsync(cancellationToken);

    public void AddQuestion(ArchitectureQuestion question) => context.Questions.Add(question);

    public Task<ArchitectureQuestion?> GetQuestionByNormalizedAsync(string normalized, CancellationToken cancellationToken) =>
        context.Questions.FirstOrDefaultAsync(q => q.Normalized == normalized, cancellationToken);

    public Task<ArchitectureQuestion?> GetQuestionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Questions.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public Task<List<ArchitectureQuestion>> GetQuestionsAsync(string? status, CancellationToken cancellationToken) =>
        context.Questions.AsNoTracking().Where(q => status == null || q.Status == status)
            .OrderByDescending(q => q.LastAskedAt).Take(1000).ToListAsync(cancellationToken);

    // ── 0052: engenharia reversa por módulo ─────────────────────────────────────────────────────

    public async Task<List<ReverseModule>> GetReverseModulesAsync(CancellationToken cancellationToken) =>
        (await SnapshotAsync(cancellationToken)).Modules.ToList();

    public Task<ReverseModule?> GetReverseModuleForUpdateAsync(string key, CancellationToken cancellationToken) =>
        context.ReverseModules.FirstOrDefaultAsync(m => m.Key == key, cancellationToken);

    public void AddReverseModule(ReverseModule module) => context.ReverseModules.Add(module);

    public async Task<List<ReverseRevisionHead>> GetRevisionHeadsAsync(string? moduleKey, string? docType, IReadOnlyCollection<string>? statuses,
        CancellationToken cancellationToken)
    {
        // 0070: as abertas (a lista e a página do módulo) saem do cache validado pela marca
        if (docType is null && statuses is { Count: 4 } && OpenStatuses.SetEquals(statuses))
            return (await SnapshotAsync(cancellationToken)).OpenRevisions.Where(r => moduleKey is null || r.ModuleKey == moduleKey).Select(r => r.Copy()).ToList();
        return await RevisionHeadsQuery(moduleKey, docType, statuses).ToListAsync(cancellationToken);
    }

    private static readonly HashSet<string> OpenStatuses =
        [ReverseRevisionStatus.Draft, ReverseRevisionStatus.Review, ReverseRevisionStatus.Changes, ReverseRevisionStatus.Approved];

    private IQueryable<ReverseRevisionHead> RevisionHeadsQuery(string? moduleKey, string? docType, IReadOnlyCollection<string>? statuses)
    {
        var query = context.ReverseRevisions.AsNoTracking();
        if (moduleKey is not null) query = query.Where(r => r.ModuleKey == moduleKey);
        if (docType is not null) query = query.Where(r => r.DocType == docType);
        if (statuses is { Count: > 0 }) query = query.Where(r => statuses.Contains(r.Status));
        return query.OrderByDescending(r => r.UpdatedAt).Take(1000).Select(r => new ReverseRevisionHead
        {
            Id = r.Id, ModuleKey = r.ModuleKey, DocType = r.DocType, Number = r.Number, Mode = r.Mode, Status = r.Status, Summary = r.Summary,
            CoverageRatio = r.CoverageRatio, Length = r.Content.Length, CreatedAt = r.CreatedAt, CreatedBy = r.CreatedBy, UpdatedAt = r.UpdatedAt,
            UpdatedBy = r.UpdatedBy, SubmittedAt = r.SubmittedAt, ReviewedBy = r.ReviewedBy, PublishedAt = r.PublishedAt, PublishedBy = r.PublishedBy,
            ReviewNote = r.ReviewNote, ProgressRaw = r.Progress, ProgressAt = r.ProgressAt
        });
    }

    public Task<ReverseRevision?> GetRevisionAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? context.ReverseRevisions : context.ReverseRevisions.AsNoTracking()).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<ReverseRevision?> GetOpenRevisionForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken) =>
        context.ReverseRevisions
            .Where(r => r.ModuleKey == moduleKey && r.DocType == docType && (r.Status == ReverseRevisionStatus.Draft || r.Status == ReverseRevisionStatus.Review
                                                                             || r.Status == ReverseRevisionStatus.Changes || r.Status == ReverseRevisionStatus.Approved))
            .OrderByDescending(r => r.Number).FirstOrDefaultAsync(cancellationToken);

    public Task<List<ReverseRevision>> GetPublishedRevisionsForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken) =>
        context.ReverseRevisions.Where(r => r.ModuleKey == moduleKey && r.DocType == docType && r.Status == ReverseRevisionStatus.Published)
            .ToListAsync(cancellationToken);

    public async Task<int> GetMaxRevisionNumberAsync(string moduleKey, string docType, CancellationToken cancellationToken) =>
        await context.ReverseRevisions.Where(r => r.ModuleKey == moduleKey && r.DocType == docType).MaxAsync(r => (int?)r.Number, cancellationToken) ?? 0;

    public void AddRevision(ReverseRevision revision) => context.ReverseRevisions.Add(revision);

    public Task<List<ReverseAssetResponse>> GetAssetHeadsAsync(string moduleKey, CancellationToken cancellationToken) =>
        context.ReverseAssets.AsNoTracking().Where(a => a.ModuleKey == moduleKey && !a.IsDeleted).OrderBy(a => a.CreatedAt)
            .Select(a => new ReverseAssetResponse
            {
                Id = a.Id, ModuleKey = a.ModuleKey, Kind = a.Kind, Title = a.Title, Url = a.Url, FileName = a.FileName, ContentType = a.ContentType,
                Size = a.Size, Notes = a.Notes, Screens = a.Screens, CreatedAt = a.CreatedAt, CreatedBy = a.CreatedBy
            }).ToListAsync(cancellationToken);

    public Task<ReverseAsset?> GetAssetAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? context.ReverseAssets : context.ReverseAssets.AsNoTracking()).FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, cancellationToken);

    public void AddAsset(ReverseAsset asset) => context.ReverseAssets.Add(asset);

    public Task<List<ReverseIndexEntry>> GetIndexEntriesAsync(string? moduleKey, CancellationToken cancellationToken) =>
        context.ReverseIndexEntries.AsNoTracking().Where(e => moduleKey == null || e.ModuleKey == moduleKey)
            .OrderBy(e => e.ModuleKey).ThenBy(e => e.DocType).ThenBy(e => e.Order).ToListAsync(cancellationToken);

    public Task<List<ReverseIndexEntry>> GetIndexEntriesByKindAsync(string kind, CancellationToken cancellationToken) =>
        context.ReverseIndexEntries.AsNoTracking().Where(e => e.Kind == kind)
            .OrderBy(e => e.ModuleKey).ThenBy(e => e.DocType).ThenBy(e => e.Order).ToListAsync(cancellationToken);

    public async Task<(int Count, DateTimeOffset? LastUpdate)> GetIndexStampAsync(CancellationToken cancellationToken) =>
        (await SnapshotAsync(cancellationToken)).IndexStamp;

    public Task<List<ReverseIndexEntry>> GetIndexEntriesForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken) =>
        context.ReverseIndexEntries.Where(e => e.ModuleKey == moduleKey && e.DocType == docType).ToListAsync(cancellationToken);

    public void AddIndexEntry(ReverseIndexEntry entry) => context.ReverseIndexEntries.Add(entry);
    public void RemoveIndexEntries(IEnumerable<ReverseIndexEntry> entries) => context.ReverseIndexEntries.RemoveRange(entries);

    public Task<ReverseInfraSnapshot?> GetInfraAsync(string moduleKey, CancellationToken cancellationToken) =>
        context.ReverseInfraSnapshots.AsNoTracking().FirstOrDefaultAsync(i => i.ModuleKey == moduleKey, cancellationToken);

    public Task<ReverseInfraSnapshot?> GetInfraForUpdateAsync(string moduleKey, CancellationToken cancellationToken) =>
        context.ReverseInfraSnapshots.FirstOrDefaultAsync(i => i.ModuleKey == moduleKey, cancellationToken);

    public void AddInfra(ReverseInfraSnapshot snapshot) => context.ReverseInfraSnapshots.Add(snapshot);

    public Task<List<ReverseTrap>> GetTrapsAsync(string? moduleKey, CancellationToken cancellationToken) =>
        context.ReverseTraps.AsNoTracking().Where(t => !t.IsDeleted && (moduleKey == null || t.ModuleKey == moduleKey))
            .OrderBy(t => t.ModuleKey).ThenBy(t => t.CreatedAt).ToListAsync(cancellationToken);

    public Task<ReverseTrap?> GetTrapForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        context.ReverseTraps.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);

    public void AddTrap(ReverseTrap trap) => context.ReverseTraps.Add(trap);

    public Task<ReverseCardContext?> GetCardContextAsync(string cardNumber, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? context.ReverseCardContexts : context.ReverseCardContexts.AsNoTracking()).FirstOrDefaultAsync(c => c.CardNumber == cardNumber, cancellationToken);

    public void AddCardContext(ReverseCardContext card) => context.ReverseCardContexts.Add(card);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await context.SaveChangesAsync(cancellationToken);
        _checked = null; // 0070: o que vier depois da gravação revalida a marca
    }

    // ── 0070: leituras da Base em cache por instância, validadas por uma marca do banco ───────────────────────
    // O banco fica em outra região (~60 ms por consulta) e a lista de módulos, a página do módulo, os projetos, o grafo e
    // o manifest começavam com as mesmas 5–8 consultas. Agora uma consulta só (a marca: quantidade + xmin das tabelas,
    // e o índice da ER por quantidade + última atualização) diz se algo mudou; sem mudança, tudo sai da memória. A marca
    // é conferida uma vez por requisição (o repositório é por requisição) e de novo depois de gravar.

    private sealed record ReadSnapshot(string Stamp, List<ArchitectureProject> Heads, List<ReverseModule> Modules, List<ReverseRevisionHead> OpenRevisions,
        List<ReverseItemCount> Counts, Dictionary<(string ProjectKey, string SectionKey), int> PendingSuggestions, (int Count, DateTimeOffset? LastUpdate) IndexStamp);

    private static ReadSnapshot? _snapshot;
    private static readonly SemaphoreSlim SnapshotGate = new(1, 1);
    private ReadSnapshot? _checked;

    private sealed class StampRow
    {
        public string Stamp { get; set; } = string.Empty;
        public int IndexCount { get; set; }
        public DateTimeOffset? IndexLast { get; set; }
    }

    private static readonly string StampSql = $"""
        SELECT concat_ws('|',
          (SELECT count(*) || ':' || coalesce(sum(xmin::text::bigint), 0) FROM "{KnowledgeContext.Schema}"."ArchitectureProjects"),
          (SELECT count(*) || ':' || coalesce(sum(xmin::text::bigint), 0) FROM "{KnowledgeContext.Schema}"."ArchitectureSections"),
          (SELECT count(*) || ':' || coalesce(sum(xmin::text::bigint), 0) FROM "{KnowledgeContext.Schema}"."ReverseModules"),
          (SELECT count(*) || ':' || coalesce(sum(xmin::text::bigint), 0) FROM "{KnowledgeContext.Schema}"."ReverseRevisions"),
          (SELECT count(*) || ':' || coalesce(sum(xmin::text::bigint), 0) FROM "{KnowledgeContext.Schema}"."ArchitectureSuggestions")) AS "Stamp",
          (SELECT count(*)::int FROM "{KnowledgeContext.Schema}"."ReverseIndexEntries") AS "IndexCount",
          (SELECT max("UpdatedAt") FROM "{KnowledgeContext.Schema}"."ReverseIndexEntries") AS "IndexLast"
        """;

    private async Task<ReadSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        if (_checked is not null) return _checked;
        var row = (await context.Database.SqlQueryRaw<StampRow>(StampSql).ToListAsync(cancellationToken)).Single();
        var stamp = $"{row.Stamp}|{row.IndexCount}:{row.IndexLast?.UtcTicks}";
        if (_snapshot is { } current && current.Stamp == stamp) return _checked = current;
        await SnapshotGate.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot is { } again && again.Stamp == stamp) return _checked = again;
            // lido depois da marca: o cache nunca fica mais velho que ela (no máximo recarrega uma vez a mais)
            var heads = await LoadProjectHeadsAsync(cancellationToken);
            var modules = await context.ReverseModules.AsNoTracking().OrderBy(m => m.Key).ToListAsync(cancellationToken);
            var open = await RevisionHeadsQuery(null, null, OpenStatuses).ToListAsync(cancellationToken);
            var counts = await context.ReverseIndexEntries.AsNoTracking().Where(e => !e.Removed)
                .GroupBy(e => new { e.ModuleKey, e.DocType, e.Kind })
                .Select(g => new ReverseItemCount(g.Key.ModuleKey, g.Key.DocType, g.Key.Kind, g.Count()))
                .ToListAsync(cancellationToken);
            var suggestions = (await context.Suggestions.AsNoTracking()
                    .Where(x => x.Status == ArchitectureSuggestionStatus.Pending && x.SectionKey != null)
                    .GroupBy(x => new { x.ProjectKey, x.SectionKey })
                    .Select(g => new { g.Key.ProjectKey, g.Key.SectionKey, Count = g.Count() })
                    .ToListAsync(cancellationToken))
                .ToDictionary(x => (x.ProjectKey, x.SectionKey!), x => x.Count);
            _snapshot = new ReadSnapshot(stamp, heads, modules, open, counts, suggestions, (row.IndexCount, row.IndexLast));
            return _checked = _snapshot;
        }
        finally
        {
            SnapshotGate.Release();
        }
    }

    // ── 0070: leituras enxutas ──────────────────────────────────────────────────────────────────

    public async Task<ArchitectureProject?> GetProjectHeadAsync(string key, CancellationToken cancellationToken) =>
        (await SnapshotAsync(cancellationToken)).Heads.FirstOrDefault(p => p.Key == key)?.ReadCopy();

    public async Task<ArchitectureSection?> GetSectionWithContentAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var head = await context.Sections.AsNoTracking().Where(s => s.Id == sectionId)
            .Select(s => new { s.Id, s.ProjectId, s.Key, s.Title, s.Order, s.ContentHash, s.Version, s.Source, s.Audience, s.UpdatedAt, s.UpdatedBy })
            .FirstOrDefaultAsync(cancellationToken);
        if (head is null) return null;
        var content = (await GetSectionContentsAsync([sectionId], cancellationToken)).GetValueOrDefault(sectionId) ?? string.Empty;
        return ArchitectureSection.WithContent(head.Id, head.ProjectId, head.Key, head.Title, head.Order, head.ContentHash, head.Version, head.Source,
            head.Audience, head.UpdatedAt, head.UpdatedBy, content);
    }

    /// <summary>
    /// 0070: texto das seções lido em streaming (<see cref="CommandBehavior.SequentialAccess"/> + <c>TextReader</c>). Pelo EF, cada
    /// texto grande alugava do <c>ArrayPool</c> compartilhado um <c>char[]</c> do tamanho do documento (2–8 MB) e o pool guardava
    /// um por thread: ~165 MB retidos depois de ler a Base para a busca (com 512 MiB, o OOM). O EF ainda guardaria o
    /// resultado inteiro (retry); aqui o retry continua pela estratégia de execução.
    /// </summary>
    public async Task<Dictionary<Guid, string>> GetSectionContentsAsync(IReadOnlyCollection<Guid> sectionIds, CancellationToken cancellationToken)
    {
        if (sectionIds.Count == 0) return [];
        var ids = sectionIds.Distinct().ToArray();
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            var result = new Dictionary<Guid, string>(ids.Length);
            var connection = context.Database.GetDbConnection();
            var opened = connection.State != System.Data.ConnectionState.Open;
            if (opened) await context.Database.OpenConnectionAsync(ct);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"""SELECT "Id", "Content" FROM "{KnowledgeContext.Schema}"."ArchitectureSections" WHERE "Id" = ANY(@ids)""";
                command.Parameters.Add(new Npgsql.NpgsqlParameter("ids", ids));
                await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, ct);
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetGuid(0);
                    using var text = reader.GetTextReader(1);
                    result[id] = await text.ReadToEndAsync(ct);
                }
            }
            finally
            {
                if (opened) await context.Database.CloseConnectionAsync();
            }
            return result;
        }, cancellationToken);
    }

    public async Task<List<string?>> GetSectionSlicesAsync(IReadOnlyList<(Guid SectionId, int Start, int Length)> slices, CancellationToken cancellationToken)
    {
        if (slices.Count == 0) return [];
        // uma consulta para todos os trechos: unnest dos arrays (id, início, tamanho) — substring em code points, 1-based
        var ids = slices.Select(s => s.SectionId).ToArray();
        var starts = slices.Select(s => Math.Max(0, s.Start) + 1).ToArray();
        var lengths = slices.Select(s => Math.Max(0, s.Length)).ToArray();
        var rows = await context.Database.SqlQueryRaw<SliceRow>(
                $"""
                 SELECT w.ord::int AS "Ord", substring(s."Content" FROM w.start FOR w.len) AS "Text"
                 FROM unnest(@ids, @starts, @lengths) WITH ORDINALITY AS w(id, start, len, ord)
                 JOIN "{KnowledgeContext.Schema}"."ArchitectureSections" s ON s."Id" = w.id
                 """,
                new Npgsql.NpgsqlParameter("ids", ids), new Npgsql.NpgsqlParameter("starts", starts), new Npgsql.NpgsqlParameter("lengths", lengths))
            .ToListAsync(cancellationToken);
        var byOrd = rows.ToDictionary(r => r.Ord, r => r.Text);
        return Enumerable.Range(1, slices.Count).Select(i => byOrd.GetValueOrDefault(i)).ToList();
    }

    private sealed class SliceRow
    {
        public int Ord { get; set; }
        public string? Text { get; set; }
    }

    public async Task<List<ReverseItemCount>> CountIndexItemsAsync(string? moduleKey, CancellationToken cancellationToken) =>
        (await SnapshotAsync(cancellationToken)).Counts.Where(c => moduleKey == null || c.ModuleKey == moduleKey).ToList();

    public async Task<List<ReverseIndexEntry>> GetIndexHeadsAsync(string? moduleKey, string? docType, CancellationToken cancellationToken)
    {
        var rows = await context.ReverseIndexEntries.AsNoTracking()
            .Where(e => (moduleKey == null || e.ModuleKey == moduleKey) && (docType == null || e.DocType == docType))
            .OrderBy(e => e.ModuleKey).ThenBy(e => e.DocType).ThenBy(e => e.Order)
            .Select(e => new { e.ModuleKey, e.DocType, e.ItemId, e.Kind, e.Title, e.Level, e.Order, e.Removed, e.Tags, e.Tables, e.Modules })
            .ToListAsync(cancellationToken);
        return rows.Select(r => ReverseIndexEntry.Head(r.ModuleKey, r.DocType, r.ItemId, r.Kind, r.Title, r.Level, r.Order, r.Removed, r.Tags, r.Tables, r.Modules))
            .ToList();
    }

    public Task<Dictionary<Guid, string>> GetIndexBodiesAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken) =>
        context.ReverseIndexEntries.AsNoTracking().Where(e => entryIds.Contains(e.Id)).Select(e => new { e.Id, e.Body })
            .ToDictionaryAsync(e => e.Id, e => e.Body, cancellationToken);

    public async Task<Dictionary<(string ProjectKey, string SectionKey), int>> CountPendingSuggestionsAsync(string? projectKey, CancellationToken cancellationToken) =>
        (await SnapshotAsync(cancellationToken)).PendingSuggestions.Where(x => projectKey == null || x.Key.ProjectKey == projectKey)
            .ToDictionary(x => x.Key, x => x.Value);

    public async Task<(int Total, int NeedsReview)> CountTrapsAsync(string moduleKey, CancellationToken cancellationToken)
    {
        var rows = await context.ReverseTraps.AsNoTracking().Where(t => !t.IsDeleted && t.ModuleKey == moduleKey)
            .GroupBy(t => t.NeedsReview).Select(g => new { NeedsReview = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return (rows.Sum(r => r.Count), rows.Where(r => r.NeedsReview).Sum(r => r.Count));
    }
}
