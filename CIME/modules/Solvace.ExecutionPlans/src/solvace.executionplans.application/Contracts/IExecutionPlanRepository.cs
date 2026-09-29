using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application.Contracts;

public interface IExecutionPlanRepository
{
    void AddPlan(ExecutionPlan plan);

    /// <summary>Plano com as etapas, rastreado (para alterar).</summary>
    Task<ExecutionPlan?> GetPlanWithStepsAsync(Guid id, CancellationToken cancellationToken);

    Task<List<ExecutionPlanSummaryResponse>> GetSummariesByCardAsync(string cardNumber, CancellationToken cancellationToken);

    /// <summary>Plano mais recente do card (qualquer status).</summary>
    Task<Guid?> GetCurrentPlanIdAsync(string cardNumber, CancellationToken cancellationToken);

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

    /// <summary>Salva; conflito de concorrência vira <see cref="ExecutionPlanConcurrencyException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Descarta o que está rastreado (antes de tentar de novo após um conflito).</summary>
    void ClearTracking();
}

/// <summary>O plano mudou entre a leitura e a gravação (outra requisição ganhou).</summary>
public class ExecutionPlanConcurrencyException(Exception inner) : Exception("Conflito de concorrência no plano.", inner);

public class ExecutionPlanNotFoundException(string message) : Exception(message);
