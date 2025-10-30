using Microsoft.Extensions.DependencyInjection;
using solvace.github.application.Services;
using solvace.github.application.Contract;

namespace solvace.github.application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitHubModule(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddScoped<IGitHubService, GitHubService>();
        return services;
    }
}


