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
        // 0099: Mesa de gestão — board, metadados e cards do DevOps pela integração de quem age.
        services.AddScoped<IDevOpsDeskService, DevOpsDeskService>();
        services.Configure<AzureDevOpsOptions>(configuration.GetSection(AzureDevOpsOptions.SectionName));
        services.AddOptions<AzureDevOpsOptions>()
            .ValidateOnStart();
        
        // O PAT não é mais fixado aqui: o AzureService monta o header a cada requisição com a
        // configuração efetiva (global ou a do usuário, quando o plugin é de uso pessoal).
        var devOps = services.AddHttpClient("AzureDevOps", client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "SolvacePRForm/1.0");
        });
        // 0099: testes locais (e2e) apontam o DevOps para um servidor falso — sem a chave, nada muda.
        if (Uri.TryCreate(configuration["AzureDevOps:HostOverride"], UriKind.Absolute, out var host))
            devOps.AddHttpMessageHandler(() => new DevOpsHostOverrideHandler(host));
        return services;
    }
}

/// <summary>
/// 0099: troca <c>https://dev.azure.com</c> (e <c>vssps.dev.azure.com</c> — usuários/identidades, ajustes 1) pelo host de
/// <c>AzureDevOps:HostOverride</c> (só e2e local).
/// </summary>
internal sealed class DevOpsHostOverrideHandler(Uri host) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri && (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase)
                                              || uri.Host.Equals("vssps.dev.azure.com", StringComparison.OrdinalIgnoreCase)))
            request.RequestUri = new UriBuilder(uri) { Scheme = host.Scheme, Host = host.Host, Port = host.Port }.Uri;
        return base.SendAsync(request, cancellationToken);
    }
}
