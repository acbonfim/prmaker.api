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

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Configuração efetiva do KC (plugin "Knowledge Center Configurations"): ambiente ativo + regras extras do filtro.</summary>
public interface IKnowledgeSettingsProvider
{
    Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken);
}

public sealed record KnowledgeSettings(string ActiveEnvironment, domain.Filtering.KnowledgeFilterOptions Filter);
