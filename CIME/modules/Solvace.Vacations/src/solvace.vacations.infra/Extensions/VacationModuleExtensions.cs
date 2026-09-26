using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using solvace.vacations.application;
using solvace.vacations.application.Contracts;
using solvace.vacations.infra.Contexts;
using solvace.vacations.infra.Repositories;

namespace solvace.vacations.infra.Extensions;

public static class VacationModuleExtensions
{
    public static IServiceCollection AddVacationModule(this IServiceCollection services, IConfiguration configuration)
    {
        // PostgreSQL, schema "vacations" (feature 0015), mesma connection string dos outros módulos do host.
        var connString = configuration.GetConnectionString("PrformDatabase");
        services.AddDbContext<VacationContext>(x => x.UseNpgsql(connString, npgsql => npgsql
            .MigrationsHistoryTable("__EFMigrationsHistory", VacationContext.Schema)
            .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null)));

        // Registra os repositórios
        services.AddScoped<IVacationRepository, VacationRepository>();
        services.AddScoped<IUserVacationBalanceRepository, UserVacationBalanceRepository>();

        // Registra a aplicação
        services.AddScoped<IVacationApplication, VacationApplication>();

        return services;
    }
}
