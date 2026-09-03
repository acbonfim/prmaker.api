using Microsoft.AspNetCore.SignalR;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Hub genérico de tempo real. Apenas gerencia associação a grupos — nenhuma regra de negócio
/// vive aqui. Os nomes dos métodos (AddToGroup/RemoveFromGroup) espelham o que o cliente
/// (WsService no frontend) já invoca.
/// </summary>
public class RealTimeHub : Hub
{
    public Task AddToGroup(string group) =>
        Groups.AddToGroupAsync(Context.ConnectionId, group);

    public Task RemoveFromGroup(string group) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
}
