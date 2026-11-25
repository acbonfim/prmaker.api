using System.Text.Json;
using Cime.BuildingBlocks.RealTime;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.PubSub.V1;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Cime.RealTime.Relay;

/// <summary>
/// Busca os eventos da API na assinatura do Pub/Sub (<see cref="RealTimeOptions.PubSubSubscription"/>) e entrega no hub,
/// como o POST /publish. A conexão sai daqui para o Google (streaming pull): o Cloud Run não precisa alcançar o
/// MonsterASP — de parte dos IPs de saída dele o relay não abre conexão. Caiu: tenta de novo (1 s → 30 s).
/// </summary>
public sealed class PubSubRelayListener(IOptions<RealTimeOptions> options, IHubContext<RealTimeHub> hub, PubSubRelayStatus status,
    TimeProvider time, ILogger<PubSubRelayListener> logger) : BackgroundService
{
    /// <summary>
    /// Evento mais velho que isso (relay parado ou reiniciando enquanto a API publicava) é descartado: chegaria fora de
    /// hora, e quem abrir a tela agora já lê o estado atual.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        // Emulador (dev/teste): PUBSUB_EMULATOR_HOST, sem credencial.
        var emulator = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PUBSUB_EMULATOR_HOST"));
        if (string.IsNullOrWhiteSpace(o.PubSubSubscription) || (!emulator && string.IsNullOrWhiteSpace(o.PubSubCredentialsJson)))
        {
            status.Disabled();
            logger.LogWarning("RealTime:PubSubSubscription/PubSubCredentialsJson vazias: eventos só pelo POST /publish");
            return;
        }

        var retry = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            SubscriberClient? subscriber = null;
            try
            {
                status.Connecting();
                subscriber = await new SubscriberClientBuilder
                {
                    SubscriptionName = SubscriptionName.Parse(o.PubSubSubscription),
                    GoogleCredential = emulator ? null : CredentialFactory.FromJson<ServiceAccountCredential>(o.PubSubCredentialsJson!).ToGoogleCredential(),
                    EmulatorDetection = emulator ? Google.Api.Gax.EmulatorDetection.EmulatorOnly : Google.Api.Gax.EmulatorDetection.ProductionOnly,
                    ClientCount = 1,
                    Settings = new SubscriberClient.Settings { FlowControlSettings = new Google.Api.Gax.FlowControlSettings(1_000, null) }
                }.BuildAsync(stoppingToken);
                await using var stop = stoppingToken.Register(() => _ = subscriber.StopAsync(new SubscriberClient.ShutdownOptions { Mode = SubscriberClient.ShutdownMode.NackImmediately, Timeout = TimeSpan.FromSeconds(5) }, CancellationToken.None));
                status.Listening();
                logger.LogInformation("Tempo real: ouvindo a assinatura {Subscription} do Pub/Sub", o.PubSubSubscription);
                retry = TimeSpan.FromSeconds(1);
                await subscriber.StartAsync((message, _) => Task.FromResult(Handle(message)));
                if (!stoppingToken.IsCancellationRequested)
                    throw new InvalidOperationException("a assinatura parou sem pedido");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                status.Failed(ex);
                logger.LogWarning(ex, "Tempo real: assinatura do Pub/Sub caiu; nova tentativa em {Retry} s", (int)retry.TotalSeconds);
                try { await Task.Delay(retry, stoppingToken); } catch (OperationCanceledException) { break; }
                retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, MaxRetry.Ticks));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            finally
            {
                if (subscriber is not null) await subscriber.DisposeAsync();
            }
        }
    }

    /// <summary>Entrega (em segundo plano, como o /publish) e confirma; mensagem inválida ou velha é só confirmada.</summary>
    public SubscriberClient.Reply Handle(PubsubMessage message)
    {
        var age = time.GetUtcNow() - message.PublishTime.ToDateTimeOffset();
        RelayPublishMessage? body;
        try
        {
            body = JsonSerializer.Deserialize<RelayPublishMessage>(message.Data.Span, RelayDelivery.JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Tempo real: mensagem {Id} do Pub/Sub inválida, descartada", message.MessageId);
            return SubscriberClient.Reply.Ack;
        }
        if (body is null || string.IsNullOrWhiteSpace(body.Event))
            return SubscriberClient.Reply.Ack;
        if (age > MaxAge)
        {
            status.Stale();
            logger.LogInformation("Tempo real: {Event} (grupo {Group}) chegou com {Age} s, descartado", body.Event, body.Group ?? "todos", (int)age.TotalSeconds);
            return SubscriberClient.Reply.Ack;
        }
        status.Received(age);
        RelayDelivery.Deliver(hub, body, logger);
        return SubscriberClient.Reply.Ack;
    }
}

/// <summary>Situação da assinatura, no GET /health do relay.</summary>
public sealed class PubSubRelayStatus(TimeProvider time)
{
    private readonly object _lock = new();
    private string _state = "starting";
    private string? _error;
    private DateTimeOffset? _since;
    private DateTimeOffset? _lastMessageAt;
    private long _received;
    private long _stale;
    private double _lastAgeMs;

    public void Disabled() => Set("disabled", null);
    public void Connecting() => Set("connecting", _error);
    public void Listening() => Set("listening", null);
    public void Failed(Exception ex) => Set("error", $"{ex.GetType().Name}: {ex.Message}");

    public void Received(TimeSpan age)
    {
        lock (_lock)
        {
            _received++;
            _lastAgeMs = Math.Round(age.TotalMilliseconds);
            _lastMessageAt = time.GetUtcNow();
        }
    }

    public void Stale()
    {
        lock (_lock) _stale++;
    }

    public object Snapshot()
    {
        lock (_lock)
            return new { state = _state, since = _since, error = _error, received = _received, stale = _stale, lastMessageAt = _lastMessageAt, lastAgeMs = _lastAgeMs };
    }

    private void Set(string state, string? error)
    {
        lock (_lock)
        {
            if (_state != state) _since = time.GetUtcNow();
            _state = state;
            _error = error is { Length: > 300 } ? error[..300] : error;
        }
    }
}
