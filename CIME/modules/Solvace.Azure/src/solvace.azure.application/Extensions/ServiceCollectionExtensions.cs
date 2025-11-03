using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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
        
        services.AddHttpClient("AzureDevOps", (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AzureDevOpsOptions>>().Value;
        
            if (!string.IsNullOrEmpty(options.PersonalAccessToken))
            {
                var authValue = Convert.ToBase64String(
                    System.Text.Encoding.ASCII.GetBytes($":{options.PersonalAccessToken}")
                );
                client.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
            }
        
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "SolvacePRForm/1.0");
        });
        return services;
    }
}


