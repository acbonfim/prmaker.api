using Microsoft.EntityFrameworkCore;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
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

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
