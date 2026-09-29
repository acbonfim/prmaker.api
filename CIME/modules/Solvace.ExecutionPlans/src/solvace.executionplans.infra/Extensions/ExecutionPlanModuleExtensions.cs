using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.infra.Contexts;
using solvace.executionplans.infra.Repositories;

namespace solvace.executionplans.infra.Extensions;

public static class ExecutionPlanModuleExtensions
{
    public static IServiceCollection AddExecutionPlanModule(this IServiceCollection services, IConfiguration configuration)
    {
        // PostgreSQL, schema "execution" (feature 0023), mesma connection string do host, contexto próprio.
        var connString = configuration.GetConnectionString("PrformDatabase");
        services.AddDbContext<ExecutionPlanContext>(x => x.UseNpgsql(connString, npgsql => npgsql
            .MigrationsHistoryTable("__EFMigrationsHistory", ExecutionPlanContext.Schema)
            .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null)));

        services.AddScoped<IExecutionPlanRepository, ExecutionPlanRepository>();
        services.AddScoped<IExecutionPlanApplication, ExecutionPlanApplication>();

        return services;
    }
}
