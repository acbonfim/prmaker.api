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
/// </remarks>
public class RelayRealTimeNotifier : IRealTimeNotifier
{
    public const string HttpClientName = "RealTimeRelay";
    public const string RelayKeyHeader = "X-Relay-Key";

    // Mesmo formato do protocolo JSON padrão do SignalR (camelCase), para o payload chegar igual ao front.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRealTimeOptionsProvider _optionsProvider;
    private readonly ILogger<RelayRealTimeNotifier> _logger;

    public RelayRealTimeNotifier(IHttpClientFactory httpClientFactory, IRealTimeOptionsProvider optionsProvider,
        ILogger<RelayRealTimeNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsProvider = optionsProvider;
        _logger = logger;
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

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.RelayUrl.TrimEnd('/')}/publish");
            if (!string.IsNullOrWhiteSpace(options.RelayKey))
                request.Headers.Add(RelayKeyHeader, options.RelayKey);
            request.Content = JsonContent.Create(new RelayPublishRequest(group, eventName, payload), options: JsonOptions);

            using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("RealTime relay respondeu {Status} ao publicar {Event} (grupo {Group})",
                    (int)response.StatusCode, eventName, group);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao publicar {Event} (grupo {Group}) no relay de tempo real", eventName, group);
        }
    }
}

/// <summary>Corpo do POST /publish do relay. <c>Group</c> nulo = todos os clientes.</summary>
public record RelayPublishRequest(string? Group, string Event, object? Payload);
