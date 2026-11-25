using System.Text.Json;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Publica os eventos do relay num tópico do Pub/Sub; o relay (MonsterASP) os busca na assinatura e entrega no hub
/// (<c>PubSubRelayListener</c>). Substitui o POST /publish: de parte dos IPs de saída do Cloud Run o relay não abre
/// conexão TCP (2026-11-25: 7 de 11 instâncias com 100% de falha, as outras com 0%), e a instância inteira ficava sem
/// tempo real. Do Cloud Run ao Pub/Sub a conexão é na rede do Google. Best-effort como o relay: falha vira log.
/// </summary>
/// <remarks>
/// O envio é aguardado dentro do request (no Cloud Run com CPU só durante requests, nada em segundo plano depois da
/// resposta), com o limite de <see cref="PublishTimeout"/>. Mesmo corpo do /publish (<see cref="RelayPublishRequest"/>).
/// </remarks>
public class PubSubRealTimeNotifier(IRealTimePubSubPublisher publisher, ILogger<PubSubRealTimeNotifier> logger,
    RealTimeDeliveryHealth? health = null) : IRealTimeNotifier
{
    public static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(3);

    // Mesmo formato do protocolo JSON padrão do SignalR (camelCase), para o payload chegar igual ao front.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task NotifyGroupAsync(string group, string eventName, object? payload = null, CancellationToken cancellationToken = default) =>
        PublishAsync(group, eventName, payload);

    public Task NotifyAllAsync(string eventName, object? payload = null, CancellationToken cancellationToken = default) =>
        PublishAsync(null, eventName, payload);

    public static byte[] Serialize(string? group, string eventName, object? payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new RelayPublishRequest(group, eventName, payload), JsonOptions);

    // O token do chamador é ignorado de propósito (como no relay HTTP): fechar a aba logo após salvar não pode impedir o aviso.
    private async Task PublishAsync(string? group, string eventName, object? payload)
    {
        try
        {
            using var limit = new CancellationTokenSource(PublishTimeout);
            await publisher.PublishAsync(Serialize(group, eventName, payload), limit.Token);
            health?.ReportSuccess();
        }
        catch (Exception ex)
        {
            health?.ReportFailure();
            logger.LogWarning(ex, "Falha ao publicar {Event} (grupo {Group}) no Pub/Sub do tempo real", eventName, group);
        }
    }
}

/// <summary>Envio de uma mensagem ao tópico do tempo real (separado para os testes).</summary>
public interface IRealTimePubSubPublisher
{
    Task PublishAsync(byte[] data, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="PublisherClient"/> do tópico <see cref="RealTimeOptions.PubSubTopic"/>, criado no primeiro envio com a
/// credencial do Cloud Run (conta de serviço de runtime). Lotes de no máximo 5 ms: o aviso sai quase na hora.
/// </summary>
public sealed class GoogleRealTimePubSubPublisher(IOptions<RealTimeOptions> options) : IRealTimePubSubPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PublisherClient? _client;

    public async Task PublishAsync(byte[] data, CancellationToken cancellationToken)
    {
        var client = await ClientAsync(cancellationToken);
        await client.PublishAsync(new PubsubMessage { Data = ByteString.CopyFrom(data) }).WaitAsync(cancellationToken);
    }

    private async Task<PublisherClient> ClientAsync(CancellationToken cancellationToken)
    {
        if (_client is { } ready) return ready;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _client ??= await new PublisherClientBuilder
            {
                TopicName = TopicName.Parse(options.Value.PubSubTopic!),
                ClientCount = 1,
                // PUBSUB_EMULATOR_HOST (dev/teste) => emulador; no Cloud Run, o Pub/Sub com a conta de serviço de runtime.
                EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
                Settings = new PublisherClient.Settings
                {
                    BatchingSettings = new BatchingSettings(elementCountThreshold: 100, byteCountThreshold: 1_000_000,
                        delayThreshold: TimeSpan.FromMilliseconds(5))
                }
            }.BuildAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is { } client)
            await client.DisposeAsync();
    }
}
