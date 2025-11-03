using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using solvace.azure.domain.Options;
using solvace.azure.domain.Requests;
using solvace.azure.application.Contract;
using Microsoft.Extensions.Http;
using System.Net.Http;
using solvace.azure.domain.Models;

namespace solvace.azure.application.Services;

public class AzureService : IAzureService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureDevOpsOptions _options;

    public AzureService(IHttpClientFactory httpClientFactory, IOptions<AzureDevOpsOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<AzureWorkItem?> GetCardAsync(string id, CancellationToken cancellationToken = default)
    {
        var baseUrl = GetAzureBaseUrl();
        var apiVersion = _options.ApiVersion;
        var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAzureAuthHeader(request);
        var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<AzureWorkItem>(content);
    }

    public async Task<AzureWorkItem?> UpdateRootCauseAsync(string id, string bodyRaw, CancellationToken cancellationToken = default)
    {
        string rootCauseText = bodyRaw ?? string.Empty;
        if (!string.IsNullOrEmpty(rootCauseText) && rootCauseText.TrimStart().StartsWith("{"))
        {
            try
            {
                var jsonRequest = JsonSerializer.Deserialize<UpdateRootCauseRequest>(rootCauseText);
                rootCauseText = jsonRequest?.RootCause ?? rootCauseText;
            }
            catch
            {
                rootCauseText = bodyRaw ?? string.Empty;
            }
        }

        var baseUrl = GetAzureBaseUrl();
        var apiVersion = _options.ApiVersion;
        var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        var patchData = new[] { new { op = "add", path = _options.RootCauseFieldPath, value = rootCauseText } };
        var json = JsonSerializer.Serialize(patchData, jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
        AddAzureAuthHeader(request);
        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<AzureWorkItem>(responseContent);
    }

    private string GetAzureBaseUrl()
    {
        if (string.IsNullOrEmpty(_options.Organization) || string.IsNullOrEmpty(_options.Project))
            throw new InvalidOperationException("Configurações do Azure DevOps não encontradas");
        var encodedProject = Uri.EscapeDataString(_options.Project);
        return $"https://dev.azure.com/{_options.Organization}/{encodedProject}/_apis";
    }

    private void AddAzureAuthHeader(HttpRequestMessage request)
    {
        if (string.IsNullOrEmpty(_options.PersonalAccessToken))
            throw new InvalidOperationException("PersonalAccessToken Azure DevOps não configurado");
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_options.PersonalAccessToken}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", "SolvacePRForm/1.0");
    }
}


