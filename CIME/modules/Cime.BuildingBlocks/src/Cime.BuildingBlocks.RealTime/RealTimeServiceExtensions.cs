using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.Configure<RealTimeOptions>(configuration.GetSection(RealTimeOptions.SectionName));

        // Provider padrão (só ambiente). O host pode registrar o seu (ex.: plugin) antes desta
        // chamada; TryAdd respeita o que já estiver registrado.
        services.TryAddSingleton<IRealTimeOptionsProvider, DefaultRealTimeOptionsProvider>();

        services.AddSignalR();
        services.AddSingleton<IRealTimeNotifier, RealTimeNotifier>();

        // Policy dedicada: a policy global usa AllowAnyOrigin(), incompatível com AllowCredentials()
        // que o SignalR precisa. Resolvida por request via ICorsPolicyProvider customizado, de modo
        // que mudanças de origens (ex.: via plugin) sejam refletidas sem reiniciar a aplicação.
        // AllowedOrigins vazio => aceita qualquer origem.
        services.AddCors();
        services.AddSingleton<ICorsPolicyProvider, RealTimeCorsPolicyProvider>();

        return services;
    }

    public static WebApplication UseRealTimeService(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IRealTimeOptionsProvider>().GetOptions();

        app.UseMiddleware<RealTimeApiKeyMiddleware>();
        app.MapHub<RealTimeHub>(options.HubPath).RequireCors(CorsPolicyName);

        return app;
    }
}
