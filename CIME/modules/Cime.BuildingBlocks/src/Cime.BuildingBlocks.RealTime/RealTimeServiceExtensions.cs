using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Extensões de registro do módulo de tempo real, no mesmo padrão dos demais building blocks
/// (AddCacheService, AddCorsPolice, etc.).
/// </summary>
public static class RealTimeServiceExtensions
{
    public const string CorsPolicyName = "RealTimeCorsPolicy";

    public static IServiceCollection AddRealTimeService(this IServiceCollection services, IConfiguration configuration)
    {
        // Ambiente (appsettings) — usado como fallback pelo provider.
        var section = configuration.GetSection(RealTimeOptions.SectionName);
        services.Configure<RealTimeOptions>(section);

        // Provider padrão (só ambiente). O host pode registrar o seu (ex.: plugin) antes desta
        // chamada; TryAdd respeita o que já estiver registrado.
        services.TryAddSingleton<IRealTimeOptionsProvider, DefaultRealTimeOptionsProvider>();
        services.AddSingleton<IRealTimeConnectionService, RealTimeConnectionService>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<RealTimeDeliveryHealth>();

        var env = section.Get<RealTimeOptions>();
        if (env?.PublishesViaPubSub == true)
        {
            // Hub no relay externo, eventos pelo Pub/Sub (o relay busca na assinatura): nenhuma conexão do Cloud Run ao relay.
            services.AddSingleton<IRealTimePubSubPublisher, GoogleRealTimePubSubPublisher>();
            services.AddSingleton<IRealTimeNotifier, PubSubRealTimeNotifier>();
            return services;
        }

        if (env?.IsRelay == true)
        {
            // Hub no relay externo: a API só publica por HTTP e não mantém WebSocket aberto.
            services.AddHttpClient(RelayRealTimeNotifier.HttpClientName, c => c.Timeout = RelayRealTimeNotifier.RequestTimeout)
                .ConfigurePrimaryHttpMessageHandler(RelayRealTimeNotifier.CreateHandler)
                // a troca de conexões fica com o PooledConnectionLifetime do handler (DNS em dia sem descartar o pool a cada 2 min)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            services.AddSingleton<IRealTimeNotifier, RelayRealTimeNotifier>();
            return services;
        }

        services.AddSignalR();
        services.AddRealTimePresence();
        services.AddSingleton<IRealTimeNotifier, RealTimeNotifier>();

        // Policy dedicada: a policy global usa AllowAnyOrigin(), incompatível com AllowCredentials()
        // que o SignalR precisa. Resolvida por request via ICorsPolicyProvider customizado, de modo
        // que mudanças de origens (ex.: via plugin) sejam refletidas sem reiniciar a aplicação.
        // AllowedOrigins vazio => aceita qualquer origem.
        services.AddCors();
        services.AddSingleton<ICorsPolicyProvider, RealTimeCorsPolicyProvider>();

        return services;
    }

    /// <summary>Presença em memória do hub (0079). Registrada onde o hub roda: em processo ou no relay.</summary>
    public static IServiceCollection AddRealTimePresence(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PresenceTracker>();
        return services;
    }

    public static WebApplication UseRealTimeService(this WebApplication app)
    {
        // No modo Relay não há hub nesta aplicação.
        if (app.Services.GetRequiredService<IOptions<RealTimeOptions>>().Value.IsRelay)
            return app;

        var options = app.Services.GetRequiredService<IRealTimeOptionsProvider>().GetOptions();

        app.UseMiddleware<RealTimeTokenMiddleware>();
        app.MapHub<RealTimeHub>(options.HubPath).RequireCors(CorsPolicyName);

        return app;
    }
}
