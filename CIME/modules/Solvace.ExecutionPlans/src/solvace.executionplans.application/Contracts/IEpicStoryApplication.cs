using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application.Contracts;

/// <summary>
/// 0099 (ajustes 2): histórias e bugs escritos no PRMake dentro de um épico (aba "Histórias" da Mesa, MCP <c>prmake_epic_stor*</c>).
/// A conversão em card é do host (cria no DevOps): <see cref="BeginConversionAsync"/> trava, o host cria o card e chama
/// <see cref="CompleteConversionAsync"/> (ou <see cref="AbortConversionAsync"/> se o DevOps recusou). Erros:
/// <see cref="ExecutionPlanNotFoundException"/> (404), <see cref="DomainException"/> (400), <see cref="EpicStoryConflictException"/>
/// (409 com <c>code</c>), <see cref="ExecutionPlanConcurrencyException"/> (409).
/// </summary>
public interface IEpicStoryApplication
{
    /// <summary>Histórias do épico (mais novas primeiro). <paramref name="status"/>: vazio = todas menos descartadas; <c>all</c> = todas.</summary>
    Task<IReadOnlyList<EpicStoryResponse>> ListAsync(long epicNumber, string? status, CancellationToken ct);
    Task<EpicStoryResponse> GetAsync(long number, CancellationToken ct);
    /// <summary>História com esse número — null se o número não é de história (resolve do <c>#n</c>).</summary>
    Task<EpicStoryResponse?> FindAsync(long number, CancellationToken ct);
    Task<EpicStoryResponse> CreateAsync(long epicNumber, SaveEpicStoryRequest req, ExecutionActor actor, CancellationToken ct);
    /// <summary>Formulário inteiro (null limpa; type/status null não mexem). Convertida → 409 <c>notEditable</c>.</summary>
    Task<EpicStoryResponse> UpdateAsync(long number, SaveEpicStoryRequest req, ExecutionActor actor, CancellationToken ct);
    /// <summary>Descarta (idempotente). Convertida → 409 <c>notEditable</c>.</summary>
    Task<EpicStoryResponse> DiscardAsync(long number, ExecutionActor actor, CancellationToken ct);
    /// <summary>Trava para converter (gravada antes de criar o card). Já convertida → 409 <c>alreadyConverted</c> com o card.</summary>
    Task<EpicStoryResponse> BeginConversionAsync(long number, ExecutionActor actor, CancellationToken ct);
    /// <summary>Card criado: marca convertida com o card e solta a trava (<c>epicsUpdated</c> action <c>stories</c>).</summary>
    Task<EpicStoryResponse> CompleteConversionAsync(long number, string cardId, ExecutionActor actor, CancellationToken ct);
    /// <summary>O DevOps recusou: solta a trava (best-effort).</summary>
    Task AbortConversionAsync(long number, CancellationToken ct);
    /// <summary>Markdown das histórias do épico para o contexto do especialista de gestão (rascunhos × convertidas).</summary>
    Task<string> ContextMarkdownAsync(long epicNumber, CancellationToken ct);
}

/// <summary>0099 (ajustes 2): histórias do épico — mesmo banco/contexto do plano (schema <c>execution</c>).</summary>
public interface IEpicStoryRepository
{
    void Add(EpicStory story);
    /// <summary>História rastreada.</summary>
    Task<EpicStory?> GetByNumberAsync(long number, CancellationToken ct);
    /// <summary>História sem rastrear (null = não é número de história).</summary>
    Task<EpicStory?> FindByNumberAsync(long number, CancellationToken ct);
    /// <summary>Histórias do épico (sem rastrear), mais novas primeiro; <paramref name="statuses"/> null = todas.</summary>
    Task<List<EpicStory>> ListByEpicAsync(Guid epicId, IReadOnlyCollection<string>? statuses, CancellationToken ct);
    /// <summary>Salva numerando as histórias novas pela sequência global (concorrência → <see cref="ExecutionPlanConcurrencyException"/>).</summary>
    Task SaveChangesAsync(CancellationToken ct);
    /// <summary>Esquece as histórias rastreadas (nova tentativa depois de conflito).</summary>
    void Detach();
}
