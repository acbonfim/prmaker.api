using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using solvace.ai.domain.Options;
using solvace.ai.application.Contract;
using solvace.ai.application.Services;
using solvace.prform.application;
using solvace.prform.domain.Extensions;
using solvace.prform.Infra.Contexts;

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
        // Resolvido por chamada (não no DI): usa a configuração efetiva do plugin do provedor,
        // inclusive a ApiKey pessoal do usuário (feature 0002/0003).
        services.AddScoped<IAIService, PluginAIService>();

        return services;
    }
}

