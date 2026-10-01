using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Responses;

namespace solvace.knowledge.application.Contracts;

public interface IKnowledgeApplication
{
    Task<KnowledgeSyncResponse> SyncAsync(KnowledgeSyncRequest request, string actor, CancellationToken cancellationToken);
    Task<KnowledgeStateResponse> GetStateAsync(string? environment, CancellationToken cancellationToken);
    Task<List<KnowledgeArticleResponse>> SearchAsync(string? term, int limit, CancellationToken cancellationToken);
    Task<KnowledgeArticleResponse> GetArticleAsync(int articleNumber, CancellationToken cancellationToken);
}

public interface IArchitectureApplication
{
    Task<List<ArchitectureProjectResponse>> ListProjectsAsync(CancellationToken cancellationToken);
    Task<ArchitectureProjectResponse> GetProjectAsync(string key, CancellationToken cancellationToken);
    Task<ArchitectureGraphResponse> GetGraphAsync(CancellationToken cancellationToken);
    Task<ArchitectureSectionResponse> GetSectionAsync(string projectKey, string sectionKey, CancellationToken cancellationToken);
    Task<ArchitectureProjectResponse> UpsertProjectAsync(string key, UpsertArchitectureProjectRequest request, string actor, CancellationToken cancellationToken);
    Task DeleteProjectAsync(string key, string actor, CancellationToken cancellationToken);
    Task<ArchitectureSectionResponse> WriteSectionAsync(string projectKey, string sectionKey, WriteArchitectureSectionRequest request, string actor, CancellationToken cancellationToken);
    Task<List<ArchitectureSectionVersionResponse>> GetVersionsAsync(string projectKey, string sectionKey, CancellationToken cancellationToken);
    Task<ArchitectureSectionVersionResponse> GetVersionAsync(string projectKey, string sectionKey, int version, CancellationToken cancellationToken);
    /// <summary>Índice compacto (markdown) de todo o parque + regras de negócio — o que a skill lê primeiro.</summary>
    Task<string> BuildIndexAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Busca no conteúdo das seções e dos artigos do KC (0037). <paramref name="extraTerms"/>: termos a mais (ex.: os que a
    /// IA sugeriu para uma pergunta); <paramref name="boostProjects"/>: projetos que sobem no ranking.
    /// </summary>
    Task<List<ArchitectureSearchHit>> SearchAsync(string query, int limit, IReadOnlyList<string>? extraTerms, IReadOnlyCollection<string>? boostProjects,
        IReadOnlyCollection<string>? boostSections,
        CancellationToken cancellationToken);

    /// <summary>Catálogo compacto (projetos e títulos das seções) para a IA entender o que existe na base.</summary>
    Task<string> BuildCatalogAsync(int maxChars, CancellationToken cancellationToken);
    Task<ArchitectureExportManifest> GetManifestAsync(CancellationToken cancellationToken);
    /// <summary>Pacote do espelho local (~/.claude/solvace-kb): índice, seções e artigos do KC.</summary>
    Task<(ArchitectureExportManifest Manifest, byte[] Zip)> ExportAsync(CancellationToken cancellationToken);
    Task<ArchitectureSuggestionResponse> SuggestAsync(CreateArchitectureSuggestionRequest request, string actor, CancellationToken cancellationToken);
    Task<List<ArchitectureSuggestionResponse>> GetSuggestionsAsync(string? status, CancellationToken cancellationToken);
    Task<ArchitectureSuggestionResponse> ResolveSuggestionAsync(Guid id, ResolveArchitectureSuggestionRequest request, string actor, CancellationToken cancellationToken);

    // 0040 — perguntas do "Pergunte"
    Task RecordQuestionAsync(string text, string? kind, string? coverage, string? suggestedProject, string? suggestedSection, string actor, CancellationToken cancellationToken);
    /// <param name="status">open | answered | dismissed | null (todas); gapsOnly = só not-found/partial.</param>
    Task<List<ArchitectureQuestionResponse>> GetQuestionsAsync(string? status, bool gapsOnly, CancellationToken cancellationToken);
    Task<ArchitectureQuestionResponse> ResolveQuestionAsync(Guid id, ResolveArchitectureQuestionRequest request, string actor, CancellationToken cancellationToken);
}
