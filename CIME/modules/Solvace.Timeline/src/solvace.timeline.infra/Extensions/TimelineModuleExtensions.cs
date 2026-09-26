using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using solvace.timeline.application;
using solvace.timeline.application.Contracts;
using solvace.timeline.infra.Contexts;
using solvace.timeline.infra.Repositories;

namespace solvace.timeline.infra.Extensions;

public static class TimelineModuleExtensions
{
    public static IServiceCollection AddTimelineModule(this IServiceCollection services, IConfiguration configuration)
    {
        // PostgreSQL, schema "timeline" (feature 0015), mesma connection string do host, contexto próprio.
        var connString = configuration.GetConnectionString("PrformDatabase");
        services.AddDbContext<TimelineContext>(x => x.UseNpgsql(connString, npgsql => npgsql
            .MigrationsHistoryTable("__EFMigrationsHistory", TimelineContext.Schema)
            .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null)));

        services.AddScoped<ITimelineRepository, TimelineRepository>();
        services.AddScoped<ITimelineApplication, TimelineApplication>();

        // Observação: IUserRepository é implementado no host (acesso à base de autenticação).

        return services;
    }
}
