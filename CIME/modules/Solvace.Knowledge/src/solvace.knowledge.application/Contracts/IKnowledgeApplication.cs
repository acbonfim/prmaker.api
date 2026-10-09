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
    /// <summary>Trecho de até <paramref name="maxChars"/> da seção (inteira se curta; grande: em volta dos termos ou o começo) — para a IA.</summary>
    Task<ArchitectureSectionResponse> GetSectionExcerptAsync(string projectKey, string sectionKey, int maxChars, IReadOnlyList<string>? terms, CancellationToken cancellationToken);
    /// <summary>0070: sumário da seção em pedaços, sem o texto.</summary>
    Task<ArchitectureSectionOutlineResponse> GetSectionOutlineAsync(string projectKey, string sectionKey, CancellationToken cancellationToken);
    /// <summary>0070: texto dos pedaços <c>from..to</c> da seção.</summary>
    Task<ArchitectureSectionPartsResponse> GetSectionPartsAsync(string projectKey, string sectionKey, int from, int to, CancellationToken cancellationToken);
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

/// <summary>Engenharia reversa por módulo (0052). <c>userRoles</c> = papéis de quem chama (aprovar/publicar pela configuração).</summary>
public interface IReverseEngineeringApplication
{
    Task<ReverseSettingsResponse> GetSettingsAsync(IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    /// <param name="withTemplate">0070: false = sem o modelo (a tela não usa; a skill usa).</param>
    Task<List<ReverseDocTypeResponse>> GetDocTypesAsync(CancellationToken cancellationToken, bool withTemplate = true);
    Task<List<ReverseModuleSummaryResponse>> ListModulesAsync(CancellationToken cancellationToken);
    Task<ReverseModuleResponse> GetModuleAsync(string key, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<ReverseModuleResponse> UpsertModuleAsync(string key, UpsertReverseModuleRequest request, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    /// <param name="withContent">0070: false = sem o texto, com o sumário em pedaços (<see cref="ReverseDocResponse.Outline"/>).</param>
    Task<ReverseDocResponse> GetDocAsync(string key, string docType, CancellationToken cancellationToken, bool withContent = true);
    /// <summary>0053: termo sugerido pelo glossário → apelido, palavra-chave ou dispensado.</summary>
    Task<ReverseModuleResponse> ResolveTermAsync(string key, ResolveReverseTermRequest request, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<ReverseSessionResponse> StartSessionAsync(string key, string docType, StartReverseSessionRequest request, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken);
    Task<List<ReverseRevisionHead>> ListRevisionsAsync(string? moduleKey, string? docType, string? status, CancellationToken cancellationToken);
    Task<ReverseRevisionResponse> GetRevisionAsync(Guid id, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<string> GetRevisionContentAsync(Guid id, CancellationToken cancellationToken);
    Task<ReverseRevisionResponse> SaveRevisionAsync(Guid id, SaveReverseRevisionRequest request, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<ReverseRevisionHead> ReportProgressAsync(Guid id, domain.Reverse.ReverseProgressUpdate update, string actor, IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken);
    Task<domain.Reverse.ReverseLintResult> LintAsync(string key, LintReverseDocumentRequest request, CancellationToken cancellationToken);
    Task<ReverseRevisionResponse> SubmitAsync(Guid id, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<ReverseRevisionResponse> ReviewAsync(Guid id, ReviewReverseRevisionRequest request, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<ReverseRevisionResponse> PublishAsync(Guid id, PublishReverseRevisionRequest request, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<ReverseAssetResponse> AddLinkAsync(string key, CreateReverseAssetLinkRequest request, string actor, CancellationToken cancellationToken);
    Task<ReverseAssetResponse> AddFileAsync(string key, string title, string fileName, string contentType, byte[] data, string? notes, IEnumerable<string>? screens,
        string actor, CancellationToken cancellationToken);
    Task<(string FileName, string ContentType, byte[] Data)> GetAssetFileAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteAssetAsync(Guid id, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<List<ReverseIndexHit>> SearchAsync(string? query, IReadOnlyCollection<string>? modules, IReadOnlyCollection<string>? kinds, string? docType, int limit,
        bool includeRemoved, CancellationToken cancellationToken);
    Task<List<ReverseItemResponse>> GetItemsAsync(IReadOnlyCollection<string> refs, string? defaultModule, CancellationToken cancellationToken);
    Task<List<ReverseIndexHit>> ImpactAsync(string term, int limit, CancellationToken cancellationToken);
    /// <summary>Texto compacto para o contexto da analisar-bug (e registra o card).</summary>
    Task<string> ForCardAsync(string card, string? moduleField, string? query, CancellationToken cancellationToken);
    Task RecordConsultedAsync(string card, IEnumerable<string> refs, CancellationToken cancellationToken);
    // 0054
    Task<ReverseInfraResponse?> GetInfraAsync(string key, CancellationToken cancellationToken);
    Task<ReverseInfraResponse> UpsertInfraAsync(string key, UpsertReverseInfraRequest request, string actor, CancellationToken cancellationToken);
    Task<List<ReverseTrapResponse>> ListTrapsAsync(string? moduleKey, CancellationToken cancellationToken);
    Task<List<ReverseTrapResponse>> CreateTrapsAsync(string key, List<CreateReverseTrapRequest> requests, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<ReverseTrapResponse> UpdateTrapAsync(Guid id, UpdateReverseTrapRequest request, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task DeleteTrapAsync(Guid id, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<ReverseTrapResponse> SuggestionToTrapAsync(Guid suggestionId, string? title, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<ReverseModuleResponse> MarkTrapsMigratedAsync(string key, string actor, IReadOnlyCollection<string> userRoles, CancellationToken cancellationToken);
    Task<ArchitectureSuggestionResponse> LinkSuggestionAsync(Guid id, LinkSuggestionItemRequest request, CancellationToken cancellationToken);
    Task RecordQuestionGapAsync(string projectKey, string question, string actor, CancellationToken cancellationToken);
    Task<List<ArchitectureSuggestionResponse>> KcDivergencesAsync(CancellationToken cancellationToken);
    /// <summary>Trava da etapa de investigação; null = pode concluir.</summary>
    Task<string?> CheckGateAsync(string card, string stepKey, string? message, CancellationToken cancellationToken);
}
