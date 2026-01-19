using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using solvace.azure.domain.Options;
using solvace.azure.domain.Requests;
using solvace.azure.application.Contract;
using Microsoft.Extensions.Http;
using System.Net.Http;
using solvace.azure.domain.Models;
using solvace.prform.application;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;

namespace solvace.azure.application.Services;

public class AzureService : IAzureService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginCacheManager _pluginCacheManager;
    private Plugin _plugin;

    public AzureService(IHttpClientFactory httpClientFactory, IPluginCacheManager pluginCacheManager)
    {
        _httpClientFactory = httpClientFactory;
        _pluginCacheManager = pluginCacheManager;
        _plugin = _pluginCacheManager.GetCachedPluginByName("AzureDevOps Configurations");
    }

    public async Task<AzureWorkItem?> GetCardAsync(string id, CancellationToken cancellationToken = default)
    {
        var baseUrl = GetAzureBaseUrl();
        var apiVersion =  _plugin.Configurations.GetConfigurationValue("ApiVersion");
        var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

        var query = $"";
        var fields = $"";
        
        url += query + fields;

        var client = _httpClientFactory.CreateClient("AzureDevOps");
        var response = await client.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var contentError = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception(contentError);
        }
        
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<AzureWorkItem>(content);
    }

    public async Task<AzureWorkItem?> UpdateRootCauseAsync(string id, UpdateRootCauseRequest bodyRaw, CancellationToken cancellationToken = default)
    {
        string rootCauseText = bodyRaw.RootCause ?? string.Empty;
        if (!string.IsNullOrEmpty(rootCauseText) && rootCauseText.TrimStart().StartsWith("{"))
        {
            try
            {
                var jsonRequest = JsonSerializer.Deserialize<UpdateRootCauseRequest>(rootCauseText);
                rootCauseText = jsonRequest?.RootCause ?? rootCauseText;
            }
            catch
            {
                rootCauseText = bodyRaw.RootCause ?? string.Empty;
            }
        }

        var baseUrl = GetAzureBaseUrl();
        var apiVersion =  _plugin.Configurations.GetConfigurationValue("ApiVersion");
        var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };
        
        var rootCauseFieldPath =  _plugin.Configurations.GetConfigurationValue("RootCauseFieldPath");

        var patchData = new[] { new { op = "add", path = rootCauseFieldPath, value = rootCauseText } };
        var json = JsonSerializer.Serialize(patchData, jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        var client = _httpClientFactory.CreateClient("AzureDevOps");
        var response = await client.PatchAsync(url, content, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
            return null;

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<AzureWorkItem>(responseContent);
    }

    private string GetAzureBaseUrl()
    {
        var organization =  _plugin.Configurations.GetConfigurationValue("Organization");
        var project =  _plugin.Configurations.GetConfigurationValue("Project");
        if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(project))
            throw new InvalidOperationException("Configurações do Azure DevOps não encontradas");
        var encodedProject = Uri.EscapeDataString(project);
        return $"https://dev.azure.com/{organization}/{encodedProject}/_apis";
    }
    
}


