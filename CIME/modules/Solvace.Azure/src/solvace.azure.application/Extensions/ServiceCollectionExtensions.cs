using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using solvace.azure.domain.Options;
using solvace.azure.application.Contract;
using solvace.azure.application.Services;

namespace solvace.azure.application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddScoped<IAzureService, AzureService>();
        services.Configure<AzureDevOpsOptions>(configuration.GetSection(AzureDevOpsOptions.SectionName));
        services.AddOptions<AzureDevOpsOptions>()
            .ValidateOnStart();
        return services;
    }
}


