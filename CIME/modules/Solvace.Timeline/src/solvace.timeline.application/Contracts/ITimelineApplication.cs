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

    Task<TimelineEntryResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<List<TimelineEntryResponse>> GetByCardNumberAsync(string cardNumber, CancellationToken cancellationToken);

    Task<TimelineEntryResponse> UpdateAsync(int id, UpdateTimelineEntryRequest request, string? updatedBy, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
