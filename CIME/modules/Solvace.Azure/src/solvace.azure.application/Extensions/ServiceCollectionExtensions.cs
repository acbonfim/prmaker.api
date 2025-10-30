using Microsoft.Extensions.DependencyInjection;
using solvace.azure.application.Contract;
using solvace.azure.application.Services;

namespace solvace.azure.application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureModule(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddScoped<IAzureService, AzureService>();
        return services;
    }
}


