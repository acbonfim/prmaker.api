using solvace.timeline.application.Contracts;
using solvace.timeline.domain.Entities;
using solvace.timeline.domain.Entities.Base;
using solvace.timeline.domain.Requests;
using solvace.timeline.domain.Responses;

namespace solvace.timeline.application;

public class TimelineApplication : ITimelineApplication
{
    private readonly ITimelineRepository _timelineRepository;
    private readonly IUserRepository _userRepository;

    public TimelineApplication(ITimelineRepository timelineRepository, IUserRepository userRepository)
    {
        _timelineRepository = timelineRepository;
        _userRepository = userRepository;
    }

    public async Task<TimelineEntryResponse> CreateAsync(
        CreateTimelineEntryRequest request,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        string userName;

        if (userId.HasValue)
        {
            // Registro interno: nome vem da base de autenticação.
            userName = await _userRepository.GetFullNameAsync(userId.Value, cancellationToken)
                       ?? throw new DomainException("Usuário logado não encontrado na base de autenticação.");
        }
        else
        {
            // Registro externo: o nome é obrigatório.
            if (string.IsNullOrWhiteSpace(request.UserName))
                throw new DomainException("O nome é obrigatório para registros externos.");

            userName = request.UserName;
        }

        var entry = new TimelineEntry(request.CardNumber, request.Description, userId, userName);
        var created = await _timelineRepository.CreateAsync(entry, cancellationToken);

        return created.ToResponse();
    }

    public async Task<TimelineEntryResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var entry = await _timelineRepository.GetByIdAsync(id, cancellationToken);
        return entry?.ToResponse();
    }

    public async Task<List<TimelineEntryResponse>> GetByCardNumberAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var entries = await _timelineRepository.GetByCardNumberAsync(cardNumber, cancellationToken);
        return entries.Select(e => e.ToResponse()).ToList();
    }

    public async Task<TimelineEntryResponse> UpdateAsync(
        int id,
        UpdateTimelineEntryRequest request,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        var entry = await _timelineRepository.GetByIdAsync(id, cancellationToken)
                    ?? throw new DomainException("Registro da linha do tempo não encontrado.");

        entry.UpdateDescription(request.Description, updatedBy);
        var updated = await _timelineRepository.UpdateAsync(entry, cancellationToken);

        return updated.ToResponse();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var entry = await _timelineRepository.GetByIdAsync(id, cancellationToken)
                    ?? throw new DomainException("Registro da linha do tempo não encontrado.");

        await _timelineRepository.DeleteAsync(entry, cancellationToken);
    }
}
