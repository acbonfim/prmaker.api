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
    Task<ArchitectureSectionResponse> GetSectionAsync(string projectKey, string sectionKey, CancellationToken cancellationToken);
    Task<ArchitectureProjectResponse> UpsertProjectAsync(string key, UpsertArchitectureProjectRequest request, string actor, CancellationToken cancellationToken);
    Task DeleteProjectAsync(string key, string actor, CancellationToken cancellationToken);
    Task<ArchitectureSectionResponse> WriteSectionAsync(string projectKey, string sectionKey, WriteArchitectureSectionRequest request, string actor, CancellationToken cancellationToken);
    Task<List<ArchitectureSectionVersionResponse>> GetVersionsAsync(string projectKey, string sectionKey, CancellationToken cancellationToken);
    Task<ArchitectureSectionVersionResponse> GetVersionAsync(string projectKey, string sectionKey, int version, CancellationToken cancellationToken);
    /// <summary>Índice compacto (markdown) de todo o parque + regras de negócio — o que a skill lê primeiro.</summary>
    Task<string> BuildIndexAsync(CancellationToken cancellationToken);
    Task<ArchitectureExportManifest> GetManifestAsync(CancellationToken cancellationToken);
    /// <summary>Pacote do espelho local (~/.claude/solvace-kb): índice, seções e artigos do KC.</summary>
    Task<(ArchitectureExportManifest Manifest, byte[] Zip)> ExportAsync(CancellationToken cancellationToken);
}
