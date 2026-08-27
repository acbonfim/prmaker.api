using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using solvace.github.domain.Options;
using solvace.github.application.Services;
using solvace.github.application.Contract;

namespace solvace.github.application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitHubModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddScoped<IGitHubService, GitHubService>();
        services.Configure<GitHubOptions>(configuration.GetSection(GitHubOptions.SectionName));
        services.AddOptions<GitHubOptions>()
            .ValidateOnStart();
        return services;
    }
}


