using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using solvace.ai.domain.Options;
using solvace.ai.application.Contract;
using solvace.ai.application.Services;

namespace solvace.ai.application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAIModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddScoped<AIServiceFactory>();
        services.Configure<AIOptions>(configuration.GetSection(AIOptions.SectionName));
        services.AddOptions<AIOptions>()
            .ValidateOnStart();
        services.AddScoped<IAIService>(serviceProvider =>
        {
            var factory = serviceProvider.GetRequiredService<AIServiceFactory>();
            return factory.CreateService();
        });

        return services;
    }
}

