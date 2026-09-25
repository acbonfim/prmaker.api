using Cime.BuildingBlocks.GlobalModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using solvace.azure.domain.Options;
using solvace.azure.application.Contract;
using solvace.azure.application.Services;
using solvace.prform.application;
using solvace.prform.domain.Extensions;

namespace solvace.azure.application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddScoped<IAzureService, AzureService>();
        services.AddScoped<IDevOpsActionsService, DevOpsActionsService>();
        services.Configure<AzureDevOpsOptions>(configuration.GetSection(AzureDevOpsOptions.SectionName));
        services.AddOptions<AzureDevOpsOptions>()
            .ValidateOnStart();
        
        // O PAT não é mais fixado aqui: o AzureService monta o header a cada requisição com a
        // configuração efetiva (global ou a do usuário, quando o plugin é de uso pessoal).
        services.AddHttpClient("AzureDevOps", client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "SolvacePRForm/1.0");
        });
        return services;
    }
}


