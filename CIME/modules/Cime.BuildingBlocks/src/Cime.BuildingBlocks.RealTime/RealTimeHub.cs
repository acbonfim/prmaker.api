using Microsoft.AspNetCore.SignalR;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Hub genérico de tempo real. Gerencia associação a grupos e a presença ao vivo (0079) — nenhuma regra de negócio
/// vive aqui. Os nomes dos métodos (AddToGroup/RemoveFromGroup) espelham o que o cliente (WsService no frontend) já
/// invoca.
/// </summary>
public class RealTimeHub(PresenceTracker presence) : Hub
{
    public Task AddToGroup(string group)
    {
        // O grupo da presença só entra pelo WatchPresence (que confere o papel admin).
        if (group.StartsWith(PresenceTracker.ReservedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new HubException("Grupo reservado");
        return Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public Task RemoveFromGroup(string group) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, group);

    public override Task OnConnectedAsync()
    {
        presence.Connected(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (presence.Disconnected(Context.ConnectionId) is { } gone)
            await Clients.Group(PresenceTracker.WatchersGroup)
                .SendAsync(PresenceTracker.RemovedEvent, new { connectionId = gone.ConnectionId, userId = gone.UserId });
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>A aba informa o estado (ativo, executando, segundo plano, ausente), a tela e o que está fazendo.</summary>
    public async Task UpdatePresence(PresenceReport report)
    {
        var user = Context.User;
        var userId = user?.FindFirst("sub")?.Value;
        var userName = user?.FindFirst(RealTimeTokens.NameClaim)?.Value;
        if (presence.Update(Context.ConnectionId, userId, userName, report, Context.GetHttpContext()) is { } entry)
            await Clients.Group(PresenceTracker.WatchersGroup).SendAsync(PresenceTracker.ChangedEvent, entry);
    }

    /// <summary>Admin passa a receber presenceChanged/presenceRemoved; devolve quem está conectado agora.</summary>
    public async Task<IReadOnlyList<PresenceEntry>> WatchPresence()
    {
        // Sem token (hub aberto, só no dev) não há papel para conferir.
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated == true && !user.IsInRole("admin"))
            throw new HubException("Somente administradores");
        await Groups.AddToGroupAsync(Context.ConnectionId, PresenceTracker.WatchersGroup);
        return presence.Snapshot();
    }

    public Task UnwatchPresence() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, PresenceTracker.WatchersGroup);
}
