using System.Diagnostics;
using System.Text.Json;
using Cime.BuildingBlocks.RealTime;
using Microsoft.AspNetCore.SignalR;

namespace Cime.RealTime.Relay;

/// <summary>Entrega de um evento da API no hub — igual pelo POST /publish e pelo Pub/Sub.</summary>
internal static class RelayDelivery
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Em segundo plano: o envio espera cada conexão do grupo, e uma lenta (aba suspensa, rede ruim) seguraria quem
    /// publicou.
    /// </summary>
    public static void Deliver(IHubContext<RealTimeHub> hub, RelayPublishMessage message, ILogger logger)
    {
        // JsonElement é repassado como está: o front recebe o mesmo JSON que a API serializou.
        object?[] args = message.Payload is { } payload ? [payload.Clone()] : [null];
        var clients = string.IsNullOrWhiteSpace(message.Group) ? hub.Clients.All : hub.Clients.Group(message.Group);
        _ = DeliverAsync(clients, message.Group, message.Event, args, logger);
    }

    private static async Task DeliverAsync(IClientProxy clients, string? group, string eventName, object?[] args, ILogger logger)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await clients.SendCoreAsync(eventName, args);
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed >= TimeSpan.FromSeconds(1))
                logger.LogWarning("Entrega lenta de {Event} (grupo {Group}): {Elapsed} ms", eventName, group ?? "todos", (long)elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao entregar {Event} (grupo {Group})", eventName, group ?? "todos");
        }
    }
}

/// <summary>Corpo do POST /publish e da mensagem do Pub/Sub (espelha <see cref="RelayPublishRequest"/> da API).</summary>
public record RelayPublishMessage(string? Group, string Event, JsonElement? Payload);
