using solvace.knowledge.domain.Entities;

namespace solvace.knowledge.application.Contracts;

public interface IKnowledgeRepository
{
    // Knowledge Center (cópia filtrada)
    Task<List<KnowledgeArticle>> GetArticlesAsync(string environment, bool tracked, CancellationToken cancellationToken);
    Task<KnowledgeArticle?> GetArticleAsync(string environment, int articleNumber, CancellationToken cancellationToken);
    Task<List<KnowledgeArticle>> SearchArticlesAsync(string environment, string? term, int limit, CancellationToken cancellationToken);
    void AddArticle(KnowledgeArticle article);
    void RemoveArticle(KnowledgeArticle article);
    Task<KnowledgeSyncState?> GetStateAsync(string environment, CancellationToken cancellationToken);
    void AddState(KnowledgeSyncState state);

    // Engenharia reversa
    /// <summary>Projetos não removidos com as seções (conteúdo incluso), sem rastreamento.</summary>
    Task<List<ArchitectureProject>> GetProjectsAsync(CancellationToken cancellationToken);
    /// <summary>Projeto pela chave com as seções, rastreado (para alterar). Inclui removidos.</summary>
    Task<ArchitectureProject?> GetProjectForUpdateAsync(string key, CancellationToken cancellationToken);
    void AddProject(ArchitectureProject project);
    void AddSection(ArchitectureSection section);
    void AddVersion(ArchitectureSectionVersion version);
    Task<List<ArchitectureSectionVersion>> GetVersionsAsync(Guid sectionId, CancellationToken cancellationToken);
    Task<ArchitectureSectionVersion?> GetVersionAsync(Guid sectionId, int version, CancellationToken cancellationToken);

    // Sugestões (fila do admin)
    void AddSuggestion(ArchitectureSuggestion suggestion);
    Task<ArchitectureSuggestion?> GetSuggestionAsync(Guid id, CancellationToken cancellationToken);
    Task<List<ArchitectureSuggestion>> GetSuggestionsAsync(string? status, CancellationToken cancellationToken);

    // Perguntas do "Pergunte" (0040)
    void AddQuestion(ArchitectureQuestion question);
    Task<ArchitectureQuestion?> GetQuestionByNormalizedAsync(string normalized, CancellationToken cancellationToken);
    Task<ArchitectureQuestion?> GetQuestionAsync(Guid id, CancellationToken cancellationToken);
    Task<List<ArchitectureQuestion>> GetQuestionsAsync(string? status, CancellationToken cancellationToken);

    // Engenharia reversa por módulo (0052)
    Task<List<ReverseModule>> GetReverseModulesAsync(CancellationToken cancellationToken);
    Task<ReverseModule?> GetReverseModuleForUpdateAsync(string key, CancellationToken cancellationToken);
    void AddReverseModule(ReverseModule module);
    /// <summary>Cabeças das revisões (sem conteúdo), filtros opcionais; mais recentes primeiro.</summary>
    Task<List<domain.Responses.ReverseRevisionHead>> GetRevisionHeadsAsync(string? moduleKey, string? docType, IReadOnlyCollection<string>? statuses,
        CancellationToken cancellationToken);
    Task<ReverseRevision?> GetRevisionAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<ReverseRevision?> GetOpenRevisionForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken);
    Task<List<ReverseRevision>> GetPublishedRevisionsForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken);
    Task<int> GetMaxRevisionNumberAsync(string moduleKey, string docType, CancellationToken cancellationToken);
    void AddRevision(ReverseRevision revision);
    /// <summary>Anexos do módulo sem o conteúdo dos arquivos.</summary>
    Task<List<domain.Responses.ReverseAssetResponse>> GetAssetHeadsAsync(string moduleKey, CancellationToken cancellationToken);
    Task<ReverseAsset?> GetAssetAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    void AddAsset(ReverseAsset asset);
    /// <summary>Itens publicados (todos, ou de um módulo), sem rastreamento.</summary>
    Task<List<ReverseIndexEntry>> GetIndexEntriesAsync(string? moduleKey, CancellationToken cancellationToken);
    /// <summary>Marca barata para saber se o índice mudou (quantidade + última atualização).</summary>
    Task<(int Count, DateTimeOffset? LastUpdate)> GetIndexStampAsync(CancellationToken cancellationToken);
    Task<List<ReverseIndexEntry>> GetIndexEntriesForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken);
    void AddIndexEntry(ReverseIndexEntry entry);
    void RemoveIndexEntries(IEnumerable<ReverseIndexEntry> entries);
    Task<ReverseCardContext?> GetCardContextAsync(string cardNumber, bool tracked, CancellationToken cancellationToken);
    void AddCardContext(ReverseCardContext context);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Configuração da engenharia reversa (0052) — plugin "Skills Configurations".</summary>
public interface IReverseSettingsProvider
{
    Task<ReverseSettings> GetAsync(CancellationToken cancellationToken);
}

/// <param name="Templates">Modelos que substituem os do código (tipo → markdown).</param>
public sealed record ReverseSettings(IReadOnlyList<string> ApproverRoles, IReadOnlyList<string> RequiredDocs, string? GateStep, double MinCoverage,
    IReadOnlyDictionary<string, string> Templates)
{
    public static ReverseSettings Default { get; } = new(["admin", "gestor"], domain.Reverse.ReverseDocTypes.DefaultRequired, "investigar-codigo", 0.9,
        new Dictionary<string, string>());
}

/// <summary>Configuração efetiva do KC (plugin "Knowledge Center Configurations"): ambiente ativo + regras extras do filtro.</summary>
public interface IKnowledgeSettingsProvider
{
    Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken);
}

public sealed record KnowledgeSettings(string ActiveEnvironment, domain.Filtering.KnowledgeFilterOptions Filter);
