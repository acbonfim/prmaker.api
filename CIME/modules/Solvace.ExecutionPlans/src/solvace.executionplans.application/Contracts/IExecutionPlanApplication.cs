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
    Task<ExecutionQuestionResponse> CancelQuestionAsync(Guid planId, Guid questionId, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionLinkResponse> AddLinkAsync(Guid planId, string stepKey, AddExecutionLinkRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionLinkResponse> UpdateLinkAsync(Guid planId, Guid linkId, UpdateExecutionLinkRequest request, ExecutionActor actor, CancellationToken cancellationToken);
    Task DeleteLinkAsync(Guid planId, Guid linkId, ExecutionActor actor, CancellationToken cancellationToken);

    /// <summary>Heartbeat da skill: registra o sinal de vida e diz se ela segue, espera ou para.</summary>
    Task<ExecutionControlResponse> ControlAsync(Guid planId, CancellationToken cancellationToken);

    /// <summary>Grava um lote de pedaços de andamento; devolve quantos eram novos.</summary>
    Task<int> AppendLogsAsync(Guid planId, AppendExecutionLogsRequest request, CancellationToken cancellationToken);
    Task<List<ExecutionLogResponse>> GetLogsAsync(Guid planId, long afterId, string? stepKey, int limit, CancellationToken cancellationToken);

    Task<ExecutionArtifactResponse> UploadArtifactAsync(Guid planId, ExecutionArtifactUpload upload, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionArtifactFile> GetArtifactFileAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken);
    Task DeleteArtifactAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken);

    // 0031 — comentários com anexos
    Task<ExecutionNoteResponse> AddNoteAsync(Guid planId, string? text, string? stepKey, IReadOnlyList<ExecutionArtifactUpload> files, ExecutionActor actor, CancellationToken cancellationToken);
    Task<ExecutionNoteResponse> EditNoteAsync(Guid planId, Guid noteId, string? text, ExecutionActor actor, CancellationToken cancellationToken);
    Task DeleteNoteAsync(Guid planId, Guid noteId, ExecutionActor actor, CancellationToken cancellationToken);
    Task<List<ExecutionNoteResponse>> GetNotesByCardAsync(string cardNumber, CancellationToken cancellationToken);

    /// <summary>Todos os arquivos do plano num .zip (uma pasta por tipo) e o nome sugerido.</summary>
    Task<(string FileName, byte[] Data)> BuildZipAsync(Guid planId, CancellationToken cancellationToken);
}
