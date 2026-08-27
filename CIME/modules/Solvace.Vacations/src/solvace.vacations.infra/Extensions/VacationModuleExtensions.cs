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
        // Registra o DbContext usando a mesma connection string
        var connString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<VacationContext>(x => x.UseMySql(connString, ServerVersion.AutoDetect(connString)));

        // Registra os repositórios
        services.AddScoped<IVacationRepository, VacationRepository>();
        services.AddScoped<IUserVacationBalanceRepository, UserVacationBalanceRepository>();

        // Registra a aplicação
        services.AddScoped<IVacationApplication, VacationApplication>();

        return services;
    }
}
