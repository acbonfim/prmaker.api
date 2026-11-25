using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Publica os eventos no relay externo (POST {RelayUrl}/publish). Best-effort: falha do relay
/// vira log de aviso e nunca quebra a operação que notificou.
/// </summary>
/// <remarks>
/// O envio é aguardado dentro do request (não fire-and-forget): no Cloud Run com CPU só durante
/// requests, trabalho em segundo plano após a resposta fica estrangulado. O timeout curto do
/// HttpClient limita o atraso. O token de cancelamento do chamador é ignorado de propósito: se o
/// usuário fechar a aba logo após salvar, os outros clientes ainda devem ser avisados.
/// Relay falhando (a última publicação falhou há menos de <see cref="DegradedWindow"/>): nenhuma requisição espera por
/// ele — antes cada falha segurava a requisição 3 s (o pedido do executor chegou a 13 s, a pergunta da Base a 11 s). Uma
/// sonda a cada <see cref="RealTimeDeliveryHealth.ProbeEvery"/> vai em segundo plano com o tempo inteiro e tira do modo
/// degradado quando passa; as outras publicações se perdem.
/// </remarks>
public class RelayRealTimeNotifier : IRealTimeNotifier
{
    public const string HttpClientName = "RealTimeRelay";
    public const string RelayKeyHeader = "X-Relay-Key";
    public static readonly TimeSpan DegradedWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    /// <summary>Conectar (TCP + TLS) ao relay; normal ~0,4 s de us-central1 até o MonsterASP.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Handler do cliente do relay. Sem <c>ConnectTimeout</c> (padrão: infinito) uma tentativa de conexão que travava — SYN
    /// perdido ou TLS parado, depois de o relay ficar parado — prendia o pool: o .NET só abre outra tentativa quando há mais
    /// requisições na fila do que tentativas pendentes, então cada publicação esperava a tentativa morta e estourava os 3 s,
    /// em sequência, até o sistema desistir (~127 s no Linux) ou o handler ser trocado (2 min). 2026-11-25: 46 de 93
    /// publicações falharam assim, em sequências de 12 a 95 s, quase sempre depois de o relay ficar parado.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectTimeout = ConnectTimeout,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90)
    };

    // Mesmo formato do protocolo JSON padrão do SignalR (camelCase), para o payload chegar igual ao front.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRealTimeOptionsProvider _optionsProvider;
    private readonly ILogger<RelayRealTimeNotifier> _logger;
    private readonly RealTimeDeliveryHealth? _health;

    public RelayRealTimeNotifier(IHttpClientFactory httpClientFactory, IRealTimeOptionsProvider optionsProvider,
        ILogger<RelayRealTimeNotifier> logger, RealTimeDeliveryHealth? health = null)
    {
        _httpClientFactory = httpClientFactory;
        _optionsProvider = optionsProvider;
        _logger = logger;
        _health = health;
    }

    public Task NotifyGroupAsync(string group, string eventName, object? payload = null, CancellationToken cancellationToken = default) =>
        PublishAsync(group, eventName, payload);

    public Task NotifyAllAsync(string eventName, object? payload = null, CancellationToken cancellationToken = default) =>
        PublishAsync(null, eventName, payload);

    private async Task PublishAsync(string? group, string eventName, object? payload)
    {
        var options = _optionsProvider.GetOptions();
        if (string.IsNullOrWhiteSpace(options.RelayUrl))
        {
            _logger.LogWarning("RealTime relay sem RelayUrl configurada; evento {Event} descartado", eventName);
            return;
        }

        if (_health?.IsDegraded(DegradedWindow) == true)
        {
            // Relay fora: ninguém espera por ele. Uma sonda a cada 15 s vai em segundo plano (tempo inteiro) e, se passar,
            // tira do modo degradado; o resto se perde (as telas e o executor também consultam).
            if (_health.TryTakeProbe())
                _ = Task.Run(() => SendAsync(options, group, eventName, payload));
            else
                _logger.LogDebug("Relay de tempo real fora: {Event} (grupo {Group}) descartado", eventName, group);
            return;
        }
        await SendAsync(options, group, eventName, payload);
    }

    private async Task SendAsync(RealTimeOptions options, string? group, string eventName, object? payload)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.RelayUrl!.TrimEnd('/')}/publish");
            if (!string.IsNullOrWhiteSpace(options.RelayKey))
                request.Headers.Add(RelayKeyHeader, options.RelayKey);
            request.Content = JsonContent.Create(new RelayPublishRequest(group, eventName, payload), options: JsonOptions);

            using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request);
            if (response.IsSuccessStatusCode)
                _health?.ReportSuccess();
            else
            {
                _health?.ReportFailure();
                _logger.LogWarning("RealTime relay respondeu {Status} ao publicar {Event} (grupo {Group})",
                    (int)response.StatusCode, eventName, group);
            }
        }
        catch (Exception ex)
        {
            _health?.ReportFailure();
            _logger.LogWarning(ex, "Falha ao publicar {Event} (grupo {Group}) no relay de tempo real", eventName, group);
        }
    }
}

/// <summary>Corpo do POST /publish do relay. <c>Group</c> nulo = todos os clientes.</summary>
public record RelayPublishRequest(string? Group, string Event, object? Payload);
