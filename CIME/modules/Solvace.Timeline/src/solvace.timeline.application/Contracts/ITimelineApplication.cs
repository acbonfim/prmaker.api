using solvace.timeline.domain.Requests;
using solvace.timeline.domain.Responses;

namespace solvace.timeline.application.Contracts;

public interface ITimelineApplication
{
    /// <summary>
    /// Cria um registro na linha do tempo. Quando <paramref name="userId"/> é informado (usuário logado),
    /// o nome é resolvido pela base de autenticação; caso contrário, o nome deve vir na requisição.
    /// </summary>
    Task<TimelineEntryResponse> CreateAsync(CreateTimelineEntryRequest request, Guid? userId, CancellationToken cancellationToken);

    /// <summary>
    /// Cria um registro externo importado (ex.: mensagem do Teams), a menos que já exista
    /// um registro com o mesmo <paramref name="sourceMessageId"/>. Retorna true se criou,
    /// false se já existia (importação duplicada ignorada).
    /// </summary>
    Task<bool> ImportExternalIfNotExistsAsync(string cardNumber, string description, string userName, string sourceMessageId, DateTimeOffset occurredAt, CancellationToken cancellationToken);

    Task<TimelineEntryResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<List<TimelineEntryResponse>> GetByCardNumberAsync(string cardNumber, CancellationToken cancellationToken);

    Task<TimelineEntryResponse> UpdateAsync(int id, UpdateTimelineEntryRequest request, string? updatedBy, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
