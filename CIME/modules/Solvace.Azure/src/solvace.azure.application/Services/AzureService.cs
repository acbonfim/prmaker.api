using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using solvace.azure.domain.Requests;
using solvace.azure.domain.ValueObjects;
using solvace.azure.application.Contract;
using Microsoft.Extensions.Http;
using System.Net.Http;

namespace solvace.azure.application.Services;

public class AzureService : IAzureService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public AzureService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<HttpJsonResponse> GetCardAsync(string id, CancellationToken cancellationToken = default)
    {
        var baseUrl = GetAzureBaseUrl();
        var apiVersion = "7.0";
        var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAzureAuthHeader(request);
        var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return HttpJsonResponse.From((int)response.StatusCode, content);
    }

    public async Task<HttpJsonResponse> UpdateRootCauseAsync(string id, string bodyRaw, CancellationToken cancellationToken = default)
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
        var apiVersion = "7.0";
        var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        var patchData = new[] { new { op = "add", path = "/fields/Custom.RCATechnicalCategorytext", value = rootCauseText } };
        var json = JsonSerializer.Serialize(patchData, jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
        AddAzureAuthHeader(request);
        var response = await client.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        return HttpJsonResponse.From((int)response.StatusCode, responseContent);
    }

    private string GetAzureBaseUrl()
    {
        var org = _configuration["AZURE_ORG"];
        var project = _configuration["AZURE_PROJECT"];
        if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(project))
            throw new InvalidOperationException("Configurações do Azure DevOps não encontradas");
        var encodedProject = Uri.EscapeDataString(project);
        return $"https://dev.azure.com/{org}/{encodedProject}/_apis";
    }

    private void AddAzureAuthHeader(HttpRequestMessage request)
    {
        var pat = _configuration["AZURE_PAT"];
        if (string.IsNullOrEmpty(pat))
            throw new InvalidOperationException("AZURE_PAT não configurado");
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", "SolvacePRForm/1.0");
    }
}


