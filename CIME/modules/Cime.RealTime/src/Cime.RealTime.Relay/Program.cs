using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cime.BuildingBlocks.RealTime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;

// Relay de tempo real (feature 0013). Hospeda o hub SignalR fora da API: no Cloud Run, WebSocket
// aberto na API mantém a instância cobrando 24 h. A API publica aqui por HTTP (POST /publish) e o
// navegador conecta em /ws com o token de curta duração emitido pela API (GET RealTime/connection).

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(RealTimeOptions.SectionName).Get<RealTimeOptions>() ?? new RealTimeOptions();
var requireToken = !string.IsNullOrWhiteSpace(options.TokenSigningKey);

builder.Services.AddSignalR();

if (requireToken)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(jwt =>
        {
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters = RealTimeTokens.CreateValidationParameters(options.TokenSigningKey!);
            jwt.Events = new JwtBearerEvents
            {
                // Upgrade WebSocket/SSE: o browser não envia headers, o SignalR manda o token na query.
                OnMessageReceived = context =>
                {
                    var token = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(options.HubPath))
                        context.Token = token;
                    return Task.CompletedTask;
                }
            };
        });
    builder.Services.AddAuthorization();
}

builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
{
    policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    if (options.AllowedOrigins is { Length: > 0 })
        policy.WithOrigins(options.AllowedOrigins);
    else
        policy.SetIsOriginAllowed(_ => true);
}));

var app = builder.Build();

if (string.IsNullOrWhiteSpace(options.RelayKey))
    app.Logger.LogWarning("RealTime:RelayKey vazia: POST /publish aberto (use só em desenvolvimento)");
if (!requireToken)
    app.Logger.LogWarning("RealTime:TokenSigningKey vazia: hub aberto sem token (use só em desenvolvimento)");

app.UseCors();
if (requireToken)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

var hub = app.MapHub<RealTimeHub>(options.HubPath);
if (requireToken)
    hub.RequireAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.MapPost("/publish", async (HttpRequest request, RelayPublishMessage message, IHubContext<RealTimeHub> hubContext) =>
{
    if (!IsRelayKeyValid(request, options.RelayKey))
        return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(message.Event))
        return Results.BadRequest(new { message = "event obrigatório" });

    // JsonElement é repassado como está: o front recebe o mesmo JSON que a API serializou.
    object?[] args = message.Payload is { } payload ? [payload] : [null];
    var clients = string.IsNullOrWhiteSpace(message.Group) ? hubContext.Clients.All : hubContext.Clients.Group(message.Group);
    await clients.SendCoreAsync(message.Event, args);
    return Results.Accepted();
});

app.Run();

static bool IsRelayKeyValid(HttpRequest request, string? expected)
{
    if (string.IsNullOrWhiteSpace(expected)) return true;
    var provided = request.Headers[RelayRealTimeNotifier.RelayKeyHeader].ToString();
    return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}

/// <summary>Corpo do POST /publish (espelha <see cref="RelayPublishRequest"/> da API).</summary>
internal record RelayPublishMessage(string? Group, string Event, JsonElement? Payload);
