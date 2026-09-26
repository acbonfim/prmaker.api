using Microsoft.AspNetCore.Http;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Valida o acesso ao hub em processo. Roda apenas no HubPath e aceita só o token de conexão
/// emitido pela API (<see cref="RealTimeTokens"/>), lido de "Authorization: Bearer" (negotiate) ou
/// da query "access_token" (upgrade WebSocket, onde o browser não envia headers). A chave fixa
/// antiga ("x-api-key") saiu na 0016. Sem TokenSigningKey configurada, o gate fica desligado (dev).
/// </summary>
public class RealTimeTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRealTimeOptionsProvider _optionsProvider;

    public RealTimeTokenMiddleware(RequestDelegate next, IRealTimeOptionsProvider optionsProvider)
    {
        _next = next;
        _optionsProvider = optionsProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _optionsProvider.GetOptions();

        // Só protege o caminho do hub; demais requisições seguem normalmente.
        if (!context.Request.Path.StartsWithSegments(options.HubPath) || string.IsNullOrWhiteSpace(options.TokenSigningKey))
        {
            await _next(context);
            return;
        }

        if (!RealTimeTokens.IsValid(ExtractToken(context.Request), options.TokenSigningKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: invalid real-time token");
            return;
        }

        await _next(context);
    }

    private static string? ExtractToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization["Bearer ".Length..].Trim();

        // Convenção padrão do SignalR para transportes sem headers customizados.
        return request.Query.TryGetValue("access_token", out var token) && !string.IsNullOrWhiteSpace(token)
            ? token.ToString()
            : null;
    }
}
