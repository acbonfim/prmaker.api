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
        // Reutiliza a mesma connection string do host (MySQL), com contexto próprio.
        var connString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<TimelineContext>(x => x.UseMySql(connString, ServerVersion.AutoDetect(connString)));

        services.AddScoped<ITimelineRepository, TimelineRepository>();
        services.AddScoped<ITimelineApplication, TimelineApplication>();

        // Observação: IUserRepository é implementado no host (acesso à base de autenticação).

        return services;
    }
}
