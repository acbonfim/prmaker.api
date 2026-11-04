using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application.Contracts;

/// <summary>Quem está agindo: nome para exibir e se é a skill (executora) ou uma pessoa na tela.</summary>
public record ExecutionActor(Guid? UserId, string Name, bool IsExecutor);

public record ExecutionArtifactUpload(string FileName, string? Kind, string? StepKey, string? Description, string ContentType, byte[] Data);

public record ExecutionArtifactFile(ExecutionArtifactResponse Artifact, byte[] Data);

public interface IExecutionPlanApplication
{
    Task<ExecutionPlanResponse> CreateAsync(CreateExecutionPlanRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task<List<ExecutionPlanSummaryResponse>> GetByCardAsync(string cardNumber, CancellationToken cancellationToken);
    Task<ExecutionPlanResponse?> GetCurrentAsync(string cardNumber, CancellationToken cancellationToken);
    Task<ExecutionPlanResponse> GetAsync(Guid planId, CancellationToken cancellationToken);

    Task<ExecutionPlanResponse> UpsertStepsAsync(Guid planId, UpsertExecutionStepsRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionStepResponse> UpdateStepAsync(Guid planId, string stepKey, UpdateExecutionStepRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionStepResponse> CancelStepAsync(Guid planId, string stepKey, string reason, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionPlanResponse> ChangeStatusAsync(Guid planId, ChangeExecutionPlanStatusRequest request, ExecutionActor actor, CancellationToken cancellationToken);

    // 0024 — ações do usuário, perguntas e links
    Task<ExecutionStepResponse> StartStepAsync(Guid planId, string stepKey, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionStepResponse> CompleteStepAsync(Guid planId, string stepKey, string? reason, ExecutionActor actor, CancellationToken cancellationToken);
    Task<List<ExecutionQuestionResponse>> AskAsync(Guid planId, AskExecutionQuestionsRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionQuestionResponse> AnswerAsync(Guid planId, Guid questionId, string answer, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionQuestionResponse> CancelQuestionAsync(Guid planId, Guid questionId, string? reason, ExecutionActor actor, CancellationToken cancellationToken);
    Task<List<ExecutionQuestionResponse>> CancelQuestionsAsync(Guid planId, CancelExecutionQuestionsRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionLinkResponse> AddLinkAsync(Guid planId, string stepKey, AddExecutionLinkRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionLinkResponse> UpdateLinkAsync(Guid planId, Guid linkId, UpdateExecutionLinkRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task DeleteLinkAsync(Guid planId, Guid linkId, ExecutionActor actor, CancellationToken cancellationToken);

    // 0075 — arquivos do card (todos os planos) e um arquivo pelo nome (imagem anexo:<nome> no texto)
    Task<List<ExecutionArtifactResponse>> GetCardArtifactsAsync(string cardNumber, CancellationToken cancellationToken);
    Task<ExecutionArtifactFile> GetCardArtifactFileByNameAsync(string cardNumber, string name, CancellationToken cancellationToken);

    // 0076 — evidências do card no DevOps guardadas no plano
    Task<CardEvidenceResponse> ImportEvidenceAsync(Guid planId, ExecutionActor actor, CancellationToken cancellationToken);
    Task<CardEvidenceResponse> ListEvidenceAsync(string cardNumber, CancellationToken cancellationToken);
    Task<CardEvidenceResponse> SkipEvidenceAsync(Guid planId, string reason, ExecutionActor actor, CancellationToken cancellationToken);
    Task<string?> CheckEvidenceAsync(Guid planId, CancellationToken cancellationToken);

    // 0075 — situação atual do card (o que está sendo aguardado) e o contexto da passagem
    Task<CardSituationResponse> GetSituationAsync(string cardNumber, bool withDevOps, CancellationToken cancellationToken);
    Task<CardSituationResponse> SetHandoffAsync(Guid planId, string? text, ExecutionActor actor, CancellationToken cancellationToken);
    /// <summary>Publica (ou atualiza) a situação na discussion do card no DevOps — manual, pela tela.</summary>
    Task<int> PublishSituationAsync(string cardNumber, string? html, ExecutionActor actor, CancellationToken cancellationToken);
    /// <summary>0076: publicação automática da situação — preferência do usuário e o padrão global.</summary>
    Task<SituationPostSettingsResponse> GetSituationPostSettingsAsync(Guid userId, CancellationToken cancellationToken);
    Task<SituationPostSettingsResponse> SetSituationPostSettingsAsync(Guid userId, bool? autoPost, CancellationToken cancellationToken);

    // 0075 — fotografias do código citado
    Task<ExecutionCodeRefResponse> SaveCodeRefAsync(Guid planId, string reference, ExecutionCodeCapture capture, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionCodeRefResponse> PatchCodeRefAsync(Guid planId, string reference, ExecutionCodeRefPatch patch, ExecutionActor actor, CancellationToken cancellationToken);
    Task DeleteCodeRefAsync(Guid planId, string reference, ExecutionActor actor, CancellationToken cancellationToken);
    Task<List<ExecutionCodeRefHeadResponse>> GetCodeRefHeadsAsync(Guid planId, CancellationToken cancellationToken);
    Task<List<ExecutionCodeRefHeadResponse>> GetCardCodeRefHeadsAsync(string cardNumber, CancellationToken cancellationToken);
    Task<ExecutionCodeRefResponse> GetCardCodeRefAsync(string cardNumber, string reference, CancellationToken cancellationToken);

    /// <summary>Heartbeat da skill: registra o sinal de vida e diz se ela segue, espera ou para.</summary>
    Task<ExecutionControlResponse> ControlAsync(Guid planId, CancellationToken cancellationToken);

    /// <summary>Grava um lote de pedaços de andamento; devolve quantos eram novos.</summary>
    Task<int> AppendLogsAsync(Guid planId, AppendExecutionLogsRequest request, CancellationToken cancellationToken);
    Task<List<ExecutionLogResponse>> GetLogsAsync(Guid planId, long afterId, string? stepKey, int limit, CancellationToken cancellationToken);

    Task<ExecutionArtifactResponse> UploadArtifactAsync(Guid planId, ExecutionArtifactUpload upload, ExecutionActor actor, CancellationToken cancellationToken);
    /// <param name="markRead">0076: quem pede é a skill — a evidência conta como aberta.</param>
    Task<ExecutionArtifactFile> GetArtifactFileAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken, bool markRead = false);
    Task DeleteArtifactAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken);

    // 0031 — comentários com anexos
    Task<ExecutionNoteResponse> AddNoteAsync(Guid planId, string? text, string? stepKey, IReadOnlyList<ExecutionArtifactUpload> files, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionNoteResponse> EditNoteAsync(Guid planId, Guid noteId, string? text, ExecutionActor actor, CancellationToken cancellationToken);
    Task DeleteNoteAsync(Guid planId, Guid noteId, ExecutionActor actor, CancellationToken cancellationToken);
    /// <param name="fromExecutor">A skill está lendo: grava até qual comentário ela leu (0037).</param>
    Task<List<ExecutionNoteResponse>> GetNotesByCardAsync(string cardNumber, CancellationToken cancellationToken, bool fromExecutor = false);

    /// <summary>Todos os arquivos do plano num .zip (uma pasta por tipo) e o nome sugerido.</summary>
    Task<(string FileName, byte[] Data)> BuildZipAsync(Guid planId, CancellationToken cancellationToken);

    // 0033: sessão do Claude Code, custo e "continuar"
    Task<ExecutionSessionResponse> RegisterSessionAsync(Guid planId, RegisterExecutionSessionRequest request, CancellationToken cancellationToken);
    /// <param name="sessionEnded">0049: consumo final mandado pelo executor depois que o processo terminou — não é sinal de
    /// sessão viva (senão a resposta/comentário dos próximos 150 s não retomaria o card).</param>
    Task<ExecutionUsageResponse> RecordUsageAsync(Guid planId, RecordExecutionUsageRequest request, CancellationToken cancellationToken, bool sessionEnded = false);
    Task<ExecutionPlanSummaryResponse> RequestResumeAsync(Guid planId, ExecutionActor actor, CancellationToken cancellationToken);
    Task AcknowledgeResumeAsync(Guid planId, CancellationToken cancellationToken);
    /// <summary>O que o vigia local desta máquina deve retomar (host = nome da máquina; null = qualquer).</summary>
    Task<ExecutionUsageReportResponse> GetUsageReportAsync(Guid? userId, int days, CancellationToken cancellationToken);
    Task<List<ExecutionResumeCandidateResponse>> GetResumeCandidatesAsync(Guid? userId, string? host, CancellationToken cancellationToken);

    // 0037: pendências do usuário
    Task<ExecutionStepResponse> ResolveStepAsync(Guid planId, string stepKey, string? note, ExecutionActor actor, CancellationToken cancellationToken);
    Task<List<ExecutionUserPendingResponse>> GetUserPendingAsync(Guid userId, CancellationToken cancellationToken);
}
