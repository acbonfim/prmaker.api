using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.infra.Contexts;
using solvace.knowledge.infra.Repositories;

namespace solvace.knowledge.infra.Extensions;

public static class KnowledgeModuleExtensions
{
    /// <summary>
    /// Base de conhecimento Solvace (feature 0033): cópia filtrada do Knowledge Center + engenharia reversa.
    /// PostgreSQL, schema "knowledge", mesma connection string do host. O host registra o IKnowledgeSettingsProvider
    /// (lê o plugin "Knowledge Center Configurations").
    /// </summary>
    public static IServiceCollection AddKnowledgeModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration.GetConnectionString("PrformDatabase");
        services.AddDbContext<KnowledgeContext>(x => x.UseNpgsql(connString, npgsql => npgsql
            .MigrationsHistoryTable("__EFMigrationsHistory", KnowledgeContext.Schema)
            .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null)));

        services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        services.AddScoped<IKnowledgeApplication, KnowledgeApplication>();
        services.AddScoped<IArchitectureApplication, ArchitectureApplication>();
        // 0052: engenharia reversa por módulo (o host registra o IReverseSettingsProvider — "Skills Configurations").
        services.AddScoped<IReverseEngineeringApplication, ReverseEngineeringApplication>();
        return services;
    }
}
