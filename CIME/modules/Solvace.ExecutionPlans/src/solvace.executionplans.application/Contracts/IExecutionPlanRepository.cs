using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application.Contracts;

public interface IExecutionPlanRepository
{
    void AddPlan(ExecutionPlan plan);

    /// <summary>Plano com as etapas, rastreado (para alterar).</summary>
    Task<ExecutionPlan?> GetPlanWithStepsAsync(Guid id, CancellationToken cancellationToken);

    Task<List<ExecutionPlanSummaryResponse>> GetSummariesByCardAsync(string cardNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Planos ativos com pedido de "continuar" pendente ou com pergunta respondida pela tela depois do último sinal
    /// da skill (0033) — do usuário informado (null = de todos).
    /// </summary>
    Task<List<(ExecutionPlan Plan, DateTimeOffset? AnsweredAt)>> GetResumeCandidatesAsync(Guid? userId, CancellationToken cancellationToken);

    /// <summary>Planos ativos (pendente, em andamento, pausado) criados pelo usuário, com as etapas, sem rastrear (0037).</summary>
    Task<List<ExecutionPlan>> GetActivePlansByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Perguntas abertas dos planos informados (0037).</summary>
    Task<List<ExecutionQuestion>> GetOpenQuestionsAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken);

    /// <summary>Planos (sem etapas, sem rastrear) atualizados desde <paramref name="since"/> — do usuário ou de todos (0041).</summary>
    Task<List<ExecutionPlan>> GetPlansWithUsageSinceAsync(Guid? userId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Plano mais recente do card (qualquer status).</summary>
    Task<Guid?> GetCurrentPlanIdAsync(string cardNumber, CancellationToken cancellationToken);
    /// <summary>0044: sessões (só leitura) de alguns planos — linha de base da sessão que passa da análise para a correção.</summary>
    Task<Dictionary<Guid, List<ExecutionSession>>> GetSessionsAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken);

    Task<long> GetLastLogIdAsync(Guid planId, CancellationToken cancellationToken);
    Task<HashSet<string>> GetExistingClientIdsAsync(Guid planId, IReadOnlyCollection<string> clientIds, CancellationToken cancellationToken);
    void AddLogs(IEnumerable<ExecutionLog> logs);
    Task<List<ExecutionLog>> GetLogsAsync(Guid planId, long afterId, string? stepKey, int limit, CancellationToken cancellationToken);

    /// <summary>Metadados dos arquivos do plano (sem o conteúdo).</summary>
    Task<List<ExecutionArtifact>> GetArtifactsAsync(Guid planId, CancellationToken cancellationToken);
    Task<ExecutionArtifact?> GetArtifactAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken);

    /// <summary>Arquivo pelo (tipo, nome), com o conteúdo, rastreado — upsert do envio.</summary>
    Task<ExecutionArtifact?> FindArtifactAsync(Guid planId, string kind, string name, CancellationToken cancellationToken);
    Task<byte[]?> GetArtifactContentAsync(Guid artifactId, CancellationToken cancellationToken);
    Task<long> GetArtifactsSizeAsync(Guid planId, Guid? excludingArtifactId, CancellationToken cancellationToken);
    void AddArtifact(ExecutionArtifact artifact);
    Task RemoveArtifactAsync(Guid artifactId, CancellationToken cancellationToken);

    // Perguntas e links (0024)
    void AddQuestions(IEnumerable<ExecutionQuestion> questions);
    Task<List<ExecutionQuestion>> GetQuestionsAsync(Guid planId, CancellationToken cancellationToken);
    Task<ExecutionQuestion?> GetQuestionAsync(Guid planId, Guid questionId, CancellationToken cancellationToken);
    void AddLink(ExecutionLink link);
    void RemoveLink(ExecutionLink link);
    Task<List<ExecutionLink>> GetLinksAsync(Guid planId, CancellationToken cancellationToken);
    Task<ExecutionLink?> GetLinkAsync(Guid planId, Guid linkId, CancellationToken cancellationToken);

    // Comentários e numeração por card (0031)
    void AddNote(ExecutionNote note);
    Task<ExecutionNote?> GetNoteAsync(Guid planId, Guid noteId, CancellationToken cancellationToken);
    /// <summary>Comentários não removidos do card (todas as fases), pelo número.</summary>
    Task<List<ExecutionNote>> GetNotesByCardAsync(string cardNumber, CancellationToken cancellationToken);

    /// <summary>A skill leu os comentários do card até <paramref name="number"/> (0037). True = avançou (era menor).</summary>
    Task<bool> MarkNotesReadAsync(string cardNumber, int number, DateTimeOffset now, CancellationToken cancellationToken);
    Task<List<ExecutionArtifact>> GetNoteAttachmentsAsync(IReadOnlyCollection<Guid> noteIds, CancellationToken cancellationToken);
    /// <summary>Anexo de comentário do card com esse conteúdo (sha256), se houver (0032).</summary>
    Task<ExecutionArtifact?> FindNoteAttachmentByShaAsync(string cardNumber, string sha256, CancellationToken cancellationToken);
    Task<int> GetMaxNoteNumberAsync(string cardNumber, CancellationToken cancellationToken);
    Task<int> GetMaxArtifactNumberAsync(string cardNumber, CancellationToken cancellationToken);
    /// <summary>Maior número e última mudança dos comentários do usuário no card (para o vigia da skill).</summary>
    Task<(int LastNumber, DateTimeOffset? ChangedAt)> GetUserNotesStateAsync(string cardNumber, CancellationToken cancellationToken);

    /// <summary>Salva; conflito de concorrência vira <see cref="ExecutionPlanConcurrencyException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Descarta o que está rastreado (antes de tentar de novo após um conflito).</summary>
    void ClearTracking();
}

/// <summary>O plano mudou entre a leitura e a gravação (outra requisição ganhou).</summary>
public class ExecutionPlanConcurrencyException(Exception inner) : Exception("Conflito de concorrência no plano.", inner);

public class ExecutionPlanNotFoundException(string message) : Exception(message);
