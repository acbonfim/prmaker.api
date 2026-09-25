using Microsoft.AspNetCore.Http;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Valida o acesso ao hub em processo. Roda apenas no HubPath. Aceita a chave compartilhada
/// (legado) ou um token de conexão emitido pela API (<see cref="RealTimeTokens"/>), lidos do
/// header "x-api-key"/"Authorization: Bearer" (negotiate) ou da query string
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

        var hasApiKey = !string.IsNullOrWhiteSpace(options.ApiKey);
        var hasTokenKey = !string.IsNullOrWhiteSpace(options.TokenSigningKey);

        // Sem chave nem token configurados => gate desligado (ambientes locais).
        if (!hasApiKey && !hasTokenKey)
        {
            await _next(context);
            return;
        }

        var provided = ExtractKey(context.Request);
        var authorized =
            (hasApiKey && string.Equals(provided, options.ApiKey, StringComparison.Ordinal)) ||
            (hasTokenKey && RealTimeTokens.IsValid(provided, options.TokenSigningKey!));

        if (!authorized)
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

        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization["Bearer ".Length..].Trim();

        if (request.Query.TryGetValue("x-api-key", out var queryKey) && !string.IsNullOrWhiteSpace(queryKey))
            return queryKey.ToString();

        // Convenção padrão do SignalR para transportes sem headers customizados.
        if (request.Query.TryGetValue("access_token", out var token) && !string.IsNullOrWhiteSpace(token))
            return token.ToString();

        return null;
    }
}
