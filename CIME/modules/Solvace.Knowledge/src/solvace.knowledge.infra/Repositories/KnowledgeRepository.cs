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

    public Task<List<ReverseModule>> GetReverseModulesAsync(CancellationToken cancellationToken) =>
        context.ReverseModules.AsNoTracking().OrderBy(m => m.Key).ToListAsync(cancellationToken);

    public Task<ReverseModule?> GetReverseModuleForUpdateAsync(string key, CancellationToken cancellationToken) =>
        context.ReverseModules.FirstOrDefaultAsync(m => m.Key == key, cancellationToken);

    public void AddReverseModule(ReverseModule module) => context.ReverseModules.Add(module);

    public Task<List<ReverseRevisionHead>> GetRevisionHeadsAsync(string? moduleKey, string? docType, IReadOnlyCollection<string>? statuses,
        CancellationToken cancellationToken)
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
        }).ToListAsync(cancellationToken);
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

    public async Task<(int Count, DateTimeOffset? LastUpdate)> GetIndexStampAsync(CancellationToken cancellationToken)
    {
        var count = await context.ReverseIndexEntries.CountAsync(cancellationToken);
        var last = count == 0 ? null : await context.ReverseIndexEntries.MaxAsync(e => (DateTimeOffset?)e.UpdatedAt, cancellationToken);
        return (count, last);
    }

    public Task<List<ReverseIndexEntry>> GetIndexEntriesForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken) =>
        context.ReverseIndexEntries.Where(e => e.ModuleKey == moduleKey && e.DocType == docType).ToListAsync(cancellationToken);

    public void AddIndexEntry(ReverseIndexEntry entry) => context.ReverseIndexEntries.Add(entry);
    public void RemoveIndexEntries(IEnumerable<ReverseIndexEntry> entries) => context.ReverseIndexEntries.RemoveRange(entries);

    public Task<ReverseCardContext?> GetCardContextAsync(string cardNumber, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? context.ReverseCardContexts : context.ReverseCardContexts.AsNoTracking()).FirstOrDefaultAsync(c => c.CardNumber == cardNumber, cancellationToken);

    public void AddCardContext(ReverseCardContext card) => context.ReverseCardContexts.Add(card);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
