using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using solvace.governance.application;
using solvace.governance.application.Contracts;
using solvace.governance.infra.Contexts;
using solvace.governance.infra.Repositories;

namespace solvace.governance.infra.Extensions;

public static class GovernanceModuleExtensions
{
    public static IServiceCollection AddGovernanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        // 0098: schema "governance", mesma connection string do host, contexto próprio.
        var connString = configuration.GetConnectionString("PrformDatabase");
        services.AddDbContext<GovernanceContext>(x => x.UseNpgsql(connString, npgsql => npgsql
            .MigrationsHistoryTable("__EFMigrationsHistory", GovernanceContext.Schema)
            .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null)));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IGovernanceRepository, GovernanceRepository>();
        services.AddScoped<IGovernanceApplication, GovernanceApplication>();
        services.AddScoped<IGovernanceStandardsApplication, GovernanceStandardsApplication>();
        services.AddSingleton<IGovernancePolicyProvider, GovernancePolicyProvider>();
        services.AddSingleton<GovernanceEventSink>();
        services.AddSingleton<IGovernanceEventSink>(sp => sp.GetRequiredService<GovernanceEventSink>());
        services.AddHostedService<GovernanceEventWriter>();

        // Observação: IGovernanceServerHookRunner (timeline/webhook) é implementado no host.
        return services;
    }
}
