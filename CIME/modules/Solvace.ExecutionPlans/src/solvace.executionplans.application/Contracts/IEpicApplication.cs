using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application.Contracts;

/// <summary>
/// 0099: épicos da Mesa de gestão (REST <c>api/v1/Epics</c>, MCP <c>prmake_epic*</c>). Os itens de trabalho do épico são os
/// itens comuns com <c>CardNumber = E{número}</c> (<see cref="EpicKey"/>). Erros: <see cref="ExecutionPlanNotFoundException"/>
/// (404), <see cref="DomainException"/> (400), <see cref="ExecutionPlanConcurrencyException"/> (409).
/// </summary>
public interface IEpicApplication
{
    /// <summary>
    /// Épicos (mais recentes primeiro). <paramref name="status"/>: um ou vários separados por vírgula; vazio = todos menos os
    /// arquivados; <c>all</c> = todos. <paramref name="query"/>: parte do título, número (<c>123</c>/<c>E123</c>) ou tag.
    /// <paramref name="createdBy"/>: só os criados por essa pessoa ("meus").
    /// </summary>
    Task<IReadOnlyList<EpicSummary>> ListAsync(string? status, string? query, Guid? createdBy, CancellationToken ct);
    Task<EpicResponse> GetAsync(long number, CancellationToken ct);
    /// <summary>Resumo do épico com esse número — null se o número não é de épico (resolve do <c>#n</c>).</summary>
    Task<EpicSummary?> FindAsync(long number, CancellationToken ct);
    Task<EpicResponse> CreateAsync(SaveEpicRequest req, ExecutionActor actor, CancellationToken ct);
    Task<EpicResponse> UpdateAsync(long number, SaveEpicRequest req, ExecutionActor actor, CancellationToken ct);
    Task<EpicResponse> SetStatusAsync(long number, string? status, ExecutionActor actor, CancellationToken ct);
    /// <summary>Põe (ou reafirma) o card no épico — papel só sobe (mentioned → linked → created).</summary>
    Task<EpicCardResponse> AddCardAsync(long number, AddEpicCardRequest req, ExecutionActor actor, CancellationToken ct);
    /// <summary>Tira o card do épico. Idempotente.</summary>
    Task RemoveCardAsync(long number, string card, ExecutionActor actor, CancellationToken ct);
    /// <summary>Épicos em que o card está (chips da Bancada).</summary>
    Task<IReadOnlyList<EpicSummary>> ListByCardAsync(string card, CancellationToken ct);
    /// <summary>
    /// Menção <c>#&lt;épico&gt;</c> num texto de um card do DevOps (conversa, Timeline, nota): dos <paramref name="numbers"/>
    /// citados, os que são épicos ganham o card como <c>mentioned</c> (sem rebaixar). Chave de épico como card não entra.
    /// Devolve quantos épicos mudaram. Best-effort — quem chama não quebra.
    /// </summary>
    Task<int> CaptureMentionsAsync(string card, IReadOnlyCollection<long> numbers, ExecutionActor actor, CancellationToken ct);
    /// <summary>Markdown do épico para o contexto do Especialista de gestão (dados, cards, board preferido).</summary>
    Task<string> ContextMarkdownAsync(long number, CancellationToken ct);
}

/// <summary>0099: épicos — mesmo banco/contexto do plano (schema <c>execution</c>).</summary>
public interface IEpicRepository
{
    void Add(Epic epic);
    /// <summary>Épico com os cards, rastreado.</summary>
    Task<Epic?> GetByNumberAsync(long number, CancellationToken ct);
    /// <summary>Épicos (sem os cards, sem rastrear) — filtros como em <see cref="IEpicApplication.ListAsync"/>, até <paramref name="limit"/>.</summary>
    Task<List<Epic>> ListAsync(IReadOnlyCollection<string>? statuses, string? query, Guid? createdBy, int limit, CancellationToken ct);
    /// <summary>Épicos (sem rastrear) com esses números.</summary>
    Task<List<Epic>> ListByNumbersAsync(IReadOnlyCollection<long> numbers, CancellationToken ct);
    /// <summary>Épicos (com os cards, rastreados) com esses números — para a menção <c>#épico</c>.</summary>
    Task<List<Epic>> GetByNumbersAsync(IReadOnlyCollection<long> numbers, CancellationToken ct);
    /// <summary>Épicos (sem rastrear) em que o card está.</summary>
    Task<List<Epic>> ListByCardAsync(string card, CancellationToken ct);
    /// <summary>Contagens por épico (cards no banco; itens pela chave <c>E{n}</c>) — uma consulta por grupo.</summary>
    Task<Dictionary<Guid, EpicCounts>> CountsAsync(IReadOnlyCollection<Epic> epics, CancellationToken ct);
    /// <summary>Salva numerando os épicos novos pela sequência global (concorrência → <see cref="ExecutionPlanConcurrencyException"/>).</summary>
    Task SaveChangesAsync(CancellationToken ct);
    /// <summary>Esquece os épicos rastreados (nova tentativa depois de conflito).</summary>
    void Detach();
}
