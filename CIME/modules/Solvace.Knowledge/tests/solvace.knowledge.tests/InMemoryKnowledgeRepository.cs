using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Responses;

namespace solvace.knowledge.tests;

/// <summary>Repositório em memória para os testes da aplicação (Base Solvace e engenharia reversa).</summary>
public sealed class InMemoryKnowledgeRepository : IKnowledgeRepository
{
    private readonly List<ArchitectureProject> _projects = [];
    private readonly List<ArchitectureSuggestion> _suggestions = [];
    private readonly List<ArchitectureQuestion> _questions = [];
    private readonly List<ReverseModule> _modules = [];
    private readonly List<ReverseRevision> _revisions = [];
    private readonly List<ReverseAsset> _assets = [];
    private readonly List<ReverseIndexEntry> _entries = [];
    private readonly List<ReverseCardContext> _cards = [];
    private int _saves;

    public Task<List<KnowledgeArticle>> GetArticlesAsync(string environment, bool tracked, CancellationToken cancellationToken) => Task.FromResult(new List<KnowledgeArticle>());
    public Task<KnowledgeArticle?> GetArticleAsync(string environment, int articleNumber, CancellationToken cancellationToken) => Task.FromResult<KnowledgeArticle?>(null);
    public Task<List<KnowledgeArticle>> SearchArticlesAsync(string environment, string? term, int limit, CancellationToken cancellationToken) => Task.FromResult(new List<KnowledgeArticle>());
    public void AddArticle(KnowledgeArticle article) { }
    public void RemoveArticle(KnowledgeArticle article) { }
    public Task<KnowledgeSyncState?> GetStateAsync(string environment, CancellationToken cancellationToken) => Task.FromResult<KnowledgeSyncState?>(null);
    public void AddState(KnowledgeSyncState state) { }
    public Task<List<ArchitectureProject>> GetProjectsAsync(CancellationToken cancellationToken) => Task.FromResult(_projects.Where(p => !p.IsDeleted).ToList());
    public Task<ArchitectureProject?> GetProjectForUpdateAsync(string key, CancellationToken cancellationToken) => Task.FromResult(_projects.FirstOrDefault(p => p.Key == key));
    public void AddProject(ArchitectureProject project) => _projects.Add(project);
    public void AddSection(ArchitectureSection section) { }
    public void AddVersion(ArchitectureSectionVersion version) { }
    public Task<List<ArchitectureSectionVersion>> GetVersionsAsync(Guid sectionId, CancellationToken cancellationToken) => Task.FromResult(new List<ArchitectureSectionVersion>());
    public Task<ArchitectureSectionVersion?> GetVersionAsync(Guid sectionId, int version, CancellationToken cancellationToken) => Task.FromResult<ArchitectureSectionVersion?>(null);
    public void AddSuggestion(ArchitectureSuggestion suggestion) => _suggestions.Add(suggestion);
    public Task<ArchitectureSuggestion?> GetSuggestionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_suggestions.FirstOrDefault(s => s.Id == id));
    public Task<List<ArchitectureSuggestion>> GetSuggestionsAsync(string? status, CancellationToken cancellationToken) =>
        Task.FromResult(_suggestions.Where(s => status == null || s.Status == status).ToList());
    public void AddQuestion(ArchitectureQuestion question) => _questions.Add(question);
    public Task<ArchitectureQuestion?> GetQuestionByNormalizedAsync(string normalized, CancellationToken cancellationToken) => Task.FromResult(_questions.FirstOrDefault(q => q.Normalized == normalized));
    public Task<ArchitectureQuestion?> GetQuestionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_questions.FirstOrDefault(q => q.Id == id));
    public Task<List<ArchitectureQuestion>> GetQuestionsAsync(string? status, CancellationToken cancellationToken) => Task.FromResult(_questions.Where(q => status == null || q.Status == status).ToList());

    public Task<List<ReverseModule>> GetReverseModulesAsync(CancellationToken cancellationToken) => Task.FromResult(_modules.ToList());
    public Task<ReverseModule?> GetReverseModuleForUpdateAsync(string key, CancellationToken cancellationToken) => Task.FromResult(_modules.FirstOrDefault(m => m.Key == key));
    public void AddReverseModule(ReverseModule module) => _modules.Add(module);

    public Task<List<ReverseRevisionHead>> GetRevisionHeadsAsync(string? moduleKey, string? docType, IReadOnlyCollection<string>? statuses, CancellationToken cancellationToken) =>
        Task.FromResult(_revisions.Where(r => (moduleKey == null || r.ModuleKey == moduleKey) && (docType == null || r.DocType == docType)
                                              && (statuses is not { Count: > 0 } || statuses.Contains(r.Status)))
            .OrderByDescending(r => r.UpdatedAt).ThenByDescending(r => r.Number)
            .Select(r => new ReverseRevisionHead
            {
                Id = r.Id, ModuleKey = r.ModuleKey, DocType = r.DocType, Number = r.Number, Mode = r.Mode, Status = r.Status, Summary = r.Summary,
                CoverageRatio = r.CoverageRatio, Length = r.Content.Length, CreatedAt = r.CreatedAt, CreatedBy = r.CreatedBy, UpdatedAt = r.UpdatedAt,
                UpdatedBy = r.UpdatedBy, SubmittedAt = r.SubmittedAt, ReviewedBy = r.ReviewedBy, PublishedAt = r.PublishedAt, PublishedBy = r.PublishedBy,
                ReviewNote = r.ReviewNote, ProgressRaw = r.Progress, ProgressAt = r.ProgressAt
            }).ToList());

    public Task<ReverseRevision?> GetRevisionAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult(_revisions.FirstOrDefault(r => r.Id == id));
    public Task<ReverseRevision?> GetOpenRevisionForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken) =>
        Task.FromResult(_revisions.Where(r => r.ModuleKey == moduleKey && r.DocType == docType && r.IsOpen).OrderByDescending(r => r.Number).FirstOrDefault());
    public Task<List<ReverseRevision>> GetPublishedRevisionsForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken) =>
        Task.FromResult(_revisions.Where(r => r.ModuleKey == moduleKey && r.DocType == docType && r.Status == ReverseRevisionStatus.Published).ToList());
    public Task<int> GetMaxRevisionNumberAsync(string moduleKey, string docType, CancellationToken cancellationToken) =>
        Task.FromResult(_revisions.Where(r => r.ModuleKey == moduleKey && r.DocType == docType).Select(r => r.Number).DefaultIfEmpty(0).Max());
    public void AddRevision(ReverseRevision revision) => _revisions.Add(revision);

    public Task<List<ReverseAssetResponse>> GetAssetHeadsAsync(string moduleKey, CancellationToken cancellationToken) =>
        Task.FromResult(_assets.Where(a => a.ModuleKey == moduleKey && !a.IsDeleted).Select(a => new ReverseAssetResponse
        {
            Id = a.Id, ModuleKey = a.ModuleKey, Kind = a.Kind, Title = a.Title, Url = a.Url, FileName = a.FileName, Size = a.Size
        }).ToList());
    public Task<ReverseAsset?> GetAssetAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult(_assets.FirstOrDefault(a => a.Id == id && !a.IsDeleted));
    public void AddAsset(ReverseAsset asset) => _assets.Add(asset);

    public Task<List<ReverseIndexEntry>> GetIndexEntriesAsync(string? moduleKey, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.Where(e => moduleKey == null || e.ModuleKey == moduleKey).OrderBy(e => e.ModuleKey).ThenBy(e => e.DocType).ThenBy(e => e.Order).ToList());
    /// <summary>Marca única por repositório (o cache estático da busca não mistura testes).</summary>
    public Task<(int Count, DateTimeOffset? LastUpdate)> GetIndexStampAsync(CancellationToken cancellationToken) =>
        Task.FromResult((_entries.Count * 1000 + _saves, (DateTimeOffset?)_stampBase.AddTicks(_saves)));
    private readonly DateTimeOffset _stampBase = DateTimeOffset.UtcNow.AddDays(Random.Shared.Next(1, 100000));
    public Task<List<ReverseIndexEntry>> GetIndexEntriesForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.Where(e => e.ModuleKey == moduleKey && e.DocType == docType).ToList());
    public void AddIndexEntry(ReverseIndexEntry entry) => _entries.Add(entry);
    public void RemoveIndexEntries(IEnumerable<ReverseIndexEntry> entries)
    {
        foreach (var e in entries.ToList()) _entries.Remove(e);
    }
    private readonly List<ReverseTrap> _traps = [];
    public Task<List<ReverseTrap>> GetTrapsAsync(string? moduleKey, CancellationToken cancellationToken) =>
        Task.FromResult(_traps.Where(t => !t.IsDeleted && (moduleKey == null || t.ModuleKey == moduleKey)).ToList());
    public Task<ReverseTrap?> GetTrapForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_traps.FirstOrDefault(t => t.Id == id && !t.IsDeleted));
    public void AddTrap(ReverseTrap trap) => _traps.Add(trap);

    public Task<ReverseCardContext?> GetCardContextAsync(string cardNumber, bool tracked, CancellationToken cancellationToken) =>
        Task.FromResult(_cards.FirstOrDefault(c => c.CardNumber == cardNumber));
    public void AddCardContext(ReverseCardContext context) => _cards.Add(context);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        _saves++;
        return Task.CompletedTask;
    }
}
