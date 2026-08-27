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

    public async Task<AzureCardFullResponse?> GetCardFullAsync(string id, CancellationToken cancellationToken = default)
    {
        var baseUrl = GetAzureBaseUrl();
        var apiVersion = _plugin.Configurations.GetConfigurationValue("ApiVersion");
        if (string.IsNullOrWhiteSpace(apiVersion))
            apiVersion = "7.0";

        var client = _httpClientFactory.CreateClient("AzureDevOps");

        // 1) Work item com todos os campos (inclui todos os custom fields)
        var workItemUrl = $"{baseUrl}/wit/workitems/{id}?$expand=all&api-version={apiVersion}";
        var wiResponse = await client.GetAsync(workItemUrl, cancellationToken);

        if (!wiResponse.IsSuccessStatusCode)
        {
            var err = await wiResponse.Content.ReadAsStringAsync(cancellationToken);
            return new AzureCardFullResponse { Error = err };
        }

        var wiContent = await wiResponse.Content.ReadAsStringAsync(cancellationToken);
        using var wiDoc = JsonDocument.Parse(wiContent);
        var root = wiDoc.RootElement;

        var result = new AzureCardFullResponse();

        if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var idVal))
            result.Id = idVal;
        if (root.TryGetProperty("rev", out var revEl) && revEl.TryGetInt64(out var revVal))
            result.Rev = revVal;

        // URL amigável de navegador (abre o work item no board do DevOps).
        result.Url = GetWorkItemBrowserUrl(id);

        if (root.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in fieldsEl.EnumerateObject())
                result.Fields[prop.Name] = prop.Value.Clone();
        }

        // 2) Comentários (best-effort — não falha o card se der erro)
        try
        {
            var commentsUrl = $"{baseUrl}/wit/workItems/{id}/comments?api-version=7.1-preview.4";
            var cResp = await client.GetAsync(commentsUrl, cancellationToken);
            if (cResp.IsSuccessStatusCode)
            {
                var cContent = await cResp.Content.ReadAsStringAsync(cancellationToken);
                using var cDoc = JsonDocument.Parse(cContent);
                if (cDoc.RootElement.TryGetProperty("comments", out var commentsEl) &&
                    commentsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in commentsEl.EnumerateArray())
                    {
                        result.Comments.Add(new AzureCardComment
                        {
                            Id = c.TryGetProperty("id", out var ci) && ci.TryGetInt64(out var civ) ? civ : 0,
                            Text = c.TryGetProperty("text", out var ct) ? ct.GetString() : null,
                            CreatedByName = GetIdentityName(c, "createdBy"),
                            CreatedDate = GetDate(c, "createdDate"),
                            ModifiedDate = GetDate(c, "modifiedDate")
                        });
                    }
                }
            }
        }
        catch { /* comentários indisponíveis — segue sem eles */ }

        // 3) Histórico de alterações (updates) (best-effort)
        try
        {
            var updatesUrl = $"{baseUrl}/wit/workItems/{id}/updates?api-version={apiVersion}";
            var uResp = await client.GetAsync(updatesUrl, cancellationToken);
            if (uResp.IsSuccessStatusCode)
            {
                var uContent = await uResp.Content.ReadAsStringAsync(cancellationToken);
                using var uDoc = JsonDocument.Parse(uContent);
                if (uDoc.RootElement.TryGetProperty("value", out var updatesEl) &&
                    updatesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in updatesEl.EnumerateArray())
                    {
                        var changes = new List<AzureCardFieldChange>();
                        if (u.TryGetProperty("fields", out var uFields) && uFields.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var f in uFields.EnumerateObject())
                            {
                                if (IgnoredHistoryFields.Contains(f.Name))
                                    continue;

                                var oldV = f.Value.TryGetProperty("oldValue", out var ov) ? ValueToString(ov) : null;
                                var newV = f.Value.TryGetProperty("newValue", out var nv) ? ValueToString(nv) : null;
                                if (oldV == null && newV == null)
                                    continue;

                                changes.Add(new AzureCardFieldChange { Field = f.Name, OldValue = oldV, NewValue = newV });
                            }
                        }

                        if (changes.Count == 0)
                            continue;

                        result.History.Add(new AzureCardHistory
                        {
                            Rev = u.TryGetProperty("rev", out var re) && re.TryGetInt32(out var rev2) ? rev2 : 0,
                            ChangedByName = GetIdentityName(u, "revisedBy") ?? GetChangedByFromFields(u),
                            ChangedDate = GetHistoryDate(u),
                            Changes = changes
                        });
                    }
                }
            }
        }
        catch { /* histórico indisponível — segue sem ele */ }

        result.Alerts = BuildAlerts(result.Fields);
        return result;
    }

    private static readonly HashSet<string> IgnoredHistoryFields = new()
    {
        "System.Rev", "System.AuthorizedDate", "System.RevisedDate", "System.ChangedDate",
        "System.Watermark", "System.AuthorizedAs", "System.ChangedBy", "System.PersonId",
        "System.CommentCount", "System.BoardColumn", "System.BoardColumnDone", "System.BoardLane"
    };

    private AzureCardAlerts BuildAlerts(Dictionary<string, JsonElement> fields)
    {
        var rootCausePath = _plugin.Configurations.GetConfigurationValue("RootCauseFieldPath");
        var rootCauseField = string.IsNullOrWhiteSpace(rootCausePath)
            ? "Custom.RCATechnicalCategorytext"
            : rootCausePath.Replace("/fields/", string.Empty).Trim('/');

        return new AzureCardAlerts
        {
            MissingRootCause = IsEmpty(fields, rootCauseField),
            MissingResolutionType = IsEmpty(fields, "Custom.ResolutionType"),
            MissingGeneralClassification = IsEmpty(fields, "Custom.GeneralClassification"),
            MissingClassification = IsEmpty(fields, "Custom.Classification"),
            RemainingNotZero = RemainingNotZero(fields, "Microsoft.VSTS.Scheduling.RemainingWork")
        };
    }

    private static bool IsEmpty(Dictionary<string, JsonElement> fields, string key)
    {
        if (!fields.TryGetValue(key, out var el))
            return true;

        return el.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            JsonValueKind.String => string.IsNullOrWhiteSpace(StripHtml(el.GetString())),
            _ => false
        };
    }

    private static bool RemainingNotZero(Dictionary<string, JsonElement> fields, string key)
    {
        if (!fields.TryGetValue(key, out var el))
            return false; // ausente → não sinaliza

        return el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var v) && v != 0m;
    }

    private static string? ValueToString(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                return el.GetRawText();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.Object:
                return el.TryGetProperty("displayName", out var dn) ? dn.GetString() : el.GetRawText();
            default:
                return el.GetRawText();
        }
    }

    private static string? GetIdentityName(JsonElement parent, string prop)
    {
        if (parent.TryGetProperty(prop, out var idEl) && idEl.ValueKind == JsonValueKind.Object &&
            idEl.TryGetProperty("displayName", out var dn))
            return dn.GetString();
        return null;
    }

    private static DateTimeOffset? GetDate(JsonElement parent, string prop)
    {
        if (parent.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(el.GetString(), out var d))
            return d;
        return null;
    }

    private static DateTimeOffset? GetHistoryDate(JsonElement update)
    {
        if (update.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object &&
            f.TryGetProperty("System.ChangedDate", out var cd) && cd.TryGetProperty("newValue", out var nv) &&
            nv.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(nv.GetString(), out var d))
            return d;

        if (update.TryGetProperty("revisedDate", out var rd) && rd.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(rd.GetString(), out var rdv) && rdv.Year < 9999)
            return rdv;

        return null;
    }

    private static string? GetChangedByFromFields(JsonElement update)
    {
        if (update.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object &&
            f.TryGetProperty("System.ChangedBy", out var cb) && cb.TryGetProperty("newValue", out var nv))
            return ValueToString(nv);
        return null;
    }

    private static string? StripHtml(string? s)
        => string.IsNullOrEmpty(s)
            ? s
            : System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", string.Empty).Trim();

    private string GetAzureBaseUrl()
    {
        var organization =  _plugin.Configurations.GetConfigurationValue("Organization");
        var project =  _plugin.Configurations.GetConfigurationValue("Project");
        if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(project))
            throw new InvalidOperationException("Configurações do Azure DevOps não encontradas");
        var encodedProject = Uri.EscapeDataString(project);
        return $"https://dev.azure.com/{organization}/{encodedProject}/_apis";
    }

    private string GetWorkItemBrowserUrl(string id)
    {
        var organization = _plugin.Configurations.GetConfigurationValue("Organization");
        var project = _plugin.Configurations.GetConfigurationValue("Project");
        var encodedProject = Uri.EscapeDataString(project);
        return $"https://dev.azure.com/{organization}/{encodedProject}/_workitems/edit/{id}";
    }
    
}


