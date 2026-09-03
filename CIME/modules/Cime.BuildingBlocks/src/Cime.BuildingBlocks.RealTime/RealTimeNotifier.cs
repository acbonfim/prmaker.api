using Microsoft.AspNetCore.SignalR;

namespace Cime.BuildingBlocks.RealTime;

public class RealTimeNotifier : IRealTimeNotifier
{
    private readonly IHubContext<RealTimeHub> _hubContext;

    public RealTimeNotifier(IHubContext<RealTimeHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyGroupAsync(string group, string eventName, object? payload = null, CancellationToken cancellationToken = default) =>
        _hubContext.Clients.Group(group).SendAsync(eventName, payload, cancellationToken);

    public Task NotifyAllAsync(string eventName, object? payload = null, CancellationToken cancellationToken = default) =>
        _hubContext.Clients.All.SendAsync(eventName, payload, cancellationToken);
}
