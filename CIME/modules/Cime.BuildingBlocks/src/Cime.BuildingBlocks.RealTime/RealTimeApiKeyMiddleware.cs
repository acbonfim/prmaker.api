using Microsoft.AspNetCore.Http;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Valida a chave compartilhada antes de permitir acesso ao hub. Roda apenas no HubPath.
/// Lê a chave do header "x-api-key" (enviado no negotiate) ou da query string
/// ("x-api-key"/"access_token") — necessária no upgrade WebSocket, onde o browser não
/// envia headers customizados. As opções são resolvidas por request via
/// <see cref="IRealTimeOptionsProvider"/> (plugin + fallback de ambiente).
/// </summary>
public class RealTimeApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRealTimeOptionsProvider _optionsProvider;

    public RealTimeApiKeyMiddleware(RequestDelegate next, IRealTimeOptionsProvider optionsProvider)
    {
        _next = next;
        _optionsProvider = optionsProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _optionsProvider.GetOptions();

        // Só protege o caminho do hub; demais requisições seguem normalmente.
        if (!context.Request.Path.StartsWithSegments(options.HubPath))
        {
            await _next(context);
            return;
        }

        // Sem chave configurada => gate desligado (ambientes locais).
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            await _next(context);
            return;
        }

        var provided = ExtractKey(context.Request);

        if (!string.Equals(provided, options.ApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: invalid WebSocket api key");
            return;
        }

        await _next(context);
    }

    private static string? ExtractKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("x-api-key", out var header) && !string.IsNullOrWhiteSpace(header))
            return header.ToString();

        if (request.Query.TryGetValue("x-api-key", out var queryKey) && !string.IsNullOrWhiteSpace(queryKey))
            return queryKey.ToString();

        // Convenção padrão do SignalR para transportes sem headers customizados.
        if (request.Query.TryGetValue("access_token", out var token) && !string.IsNullOrWhiteSpace(token))
            return token.ToString();

        return null;
    }
}
