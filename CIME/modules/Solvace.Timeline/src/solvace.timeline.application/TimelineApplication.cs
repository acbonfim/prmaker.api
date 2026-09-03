using Cime.BuildingBlocks.RealTime;
using solvace.timeline.application.Contracts;
using solvace.timeline.domain.Entities;
using solvace.timeline.domain.Entities.Base;
using solvace.timeline.domain.RealTime;
using solvace.timeline.domain.Requests;
using solvace.timeline.domain.Responses;

namespace solvace.timeline.application;

public class TimelineApplication : ITimelineApplication
{
    private readonly ITimelineRepository _timelineRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRealTimeNotifier _realTimeNotifier;

    public TimelineApplication(
        ITimelineRepository timelineRepository,
        IUserRepository userRepository,
        IRealTimeNotifier realTimeNotifier)
    {
        _timelineRepository = timelineRepository;
        _userRepository = userRepository;
        _realTimeNotifier = realTimeNotifier;
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

        await NotifyTimelineUpdatedAsync(created.CardNumber, TimelineRealTimeEvents.Actions.Create, created.Id, cancellationToken);

        return created.ToResponse();
    }

    public async Task<bool> ImportExternalIfNotExistsAsync(
        string cardNumber,
        string description,
        string userName,
        string sourceMessageId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceMessageId))
            throw new DomainException("O id da mensagem de origem é obrigatório.");

        if (await _timelineRepository.ExistsBySourceMessageIdAsync(sourceMessageId, cancellationToken))
            return false;

        // Respeita o limite da coluna Description (2000).
        var trimmed = (description ?? string.Empty).Trim();
        if (trimmed.Length > 2000)
            trimmed = trimmed[..2000];

        var entry = new TimelineEntry(cardNumber, trimmed, userName, sourceMessageId, occurredAt);
        var created = await _timelineRepository.CreateAsync(entry, cancellationToken);

        await NotifyTimelineUpdatedAsync(cardNumber, TimelineRealTimeEvents.Actions.Create, created.Id, cancellationToken);

        return true;
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

        await NotifyTimelineUpdatedAsync(updated.CardNumber, TimelineRealTimeEvents.Actions.Update, updated.Id, cancellationToken);

        return updated.ToResponse();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var entry = await _timelineRepository.GetByIdAsync(id, cancellationToken)
                    ?? throw new DomainException("Registro da linha do tempo não encontrado.");

        await _timelineRepository.DeleteAsync(entry, cancellationToken);

        await NotifyTimelineUpdatedAsync(entry.CardNumber, TimelineRealTimeEvents.Actions.Delete, entry.Id, cancellationToken);
    }

    /// <summary>
    /// Avisa (fire-and-forget seguro) quem está no card que a timeline mudou. O frontend
    /// refaz o GET autenticado — nenhum conteúdo trafega pelo WS. O <paramref name="action"/> e o
    /// <paramref name="entryId"/> permitem ao cliente mostrar o skeleton no lugar certo
    /// (comentário novo no fim vs. o comentário específico editado/excluído). Nunca quebra a operação.
    /// </summary>
    private async Task NotifyTimelineUpdatedAsync(string cardNumber, string action, int entryId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
            return;

        try
        {
            await _realTimeNotifier.NotifyGroupAsync(
                TimelineRealTimeEvents.Group(cardNumber),
                TimelineRealTimeEvents.EventTimelineUpdated,
                new { cardNumber, action, entryId },
                cancellationToken);
        }
        catch
        {
            // Notificação em tempo real é best-effort; falha aqui não deve afetar a operação principal.
        }
    }
}
