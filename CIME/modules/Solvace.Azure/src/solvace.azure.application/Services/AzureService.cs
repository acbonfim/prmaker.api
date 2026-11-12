using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Options;
using solvace.azure.domain.Requests;
using solvace.azure.application.Contract;
using Microsoft.Extensions.Http;
using System.Net.Http;
using solvace.azure.domain.Models;
using solvace.prform.application;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;

namespace solvace.azure.application.Services;

public class AzureService : IAzureService
{
    private const string PluginName = "AzureDevOps Configurations";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginConfigurationResolver _configurationResolver;

    // Configuração efetiva da requisição: global, ou a do usuário quando o plugin é de uso pessoal
    // (feature 0002). Resolvida no início de cada operação (EnsureConfigAsync).
    private PluginConfiguration? _config;

    public AzureService(IHttpClientFactory httpClientFactory, IPluginConfigurationResolver configurationResolver)
    {
        _httpClientFactory = httpClientFactory;
        _configurationResolver = configurationResolver;
    }

    /// <summary>
    /// Resolve a configuração (lança PersonalIntegrationRequiredException → 403 quando o plugin é
    /// pessoal e o usuário não configurou). Chamar no início de cada operação, fora de try/catch.
    /// </summary>
    private async Task EnsureConfigAsync(CancellationToken cancellationToken)
    {
        _config ??= await _configurationResolver.GetEffectiveConfigurationAsync(PluginName, cancellationToken);
    }

    private PluginConfiguration Config =>
        _config ?? throw new InvalidOperationException("Configuração do Azure DevOps não resolvida");

    public async Task<AzureDevOpsFieldNames> GetFieldNamesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        return FieldNames;
    }

    private AzureDevOpsFieldNames FieldNames
    {
        get
        {
            string Field(string key)
            {
                var value = Config.GetConfigurationValueOrDefault(key, string.Empty);
                return string.IsNullOrWhiteSpace(value) ? AzureDevOpsFieldKeys.Defaults[key] : value.Trim();
            }

            return new AzureDevOpsFieldNames(
                Field(AzureDevOpsFieldKeys.ResolutionType),
                Field(AzureDevOpsFieldKeys.GeneralClassification),
                Field(AzureDevOpsFieldKeys.Classification),
                Field(AzureDevOpsFieldKeys.RemainingWork),
                Field(AzureDevOpsFieldKeys.OriginalEstimate),
                Field(AzureDevOpsFieldKeys.CompletedWork),
                Field(AzureDevOpsFieldKeys.RootCauseFieldPath).Replace("/fields/", string.Empty).Trim('/'),
                Field(AzureDevOpsFieldKeys.BugType));
        }
    }

    /// <summary>Cliente com o PAT da configuração efetiva (por requisição, não mais fixo no HttpClient nomeado).</summary>
    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("AzureDevOps");
        var personalAccessToken = Config.GetConfigurationValue("PersonalAccessToken");
        if (!string.IsNullOrEmpty(personalAccessToken))
        {
            var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{personalAccessToken}"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
        }
        return client;
    }

    public async Task<IReadOnlyList<int>> QueryWorkItemIdsAsync(string wiql, int top, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var apiVersion = Config.GetConfigurationValue("ApiVersion");
        var url = $"{GetAzureBaseUrl()}/wit/wiql?api-version={apiVersion}&$top={Math.Clamp(top, 1, 200)}";
        var body = new StringContent(JsonSerializer.Serialize(new { query = wiql }), Encoding.UTF8, "application/json");
        var response = await CreateClient().PostAsync(url, body, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Azure DevOps (WIQL) respondeu {(int)response.StatusCode}: {content[..Math.Min(content.Length, 500)]}");
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.TryGetProperty("workItems", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList()
            : [];
    }

    public async Task<IReadOnlyList<AzureCardSummaryResponse>> GetCardsSummaryAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        var distinct = ids.Where(i => i > 0).Distinct().Take(200).ToList();
        if (distinct.Count == 0) return [];
        await EnsureConfigAsync(cancellationToken);

        var apiVersion = Config.GetConfigurationValue("ApiVersion");
        var url = $"{GetAzureBaseUrl()}/wit/workitemsbatch?api-version={apiVersion}";
        var payload = new
        {
            ids = distinct,
            fields = new[] { "System.Id", "System.Title", "System.State", "System.BoardColumn", "System.WorkItemType", "System.AssignedTo", "System.ChangedDate" },
            // Card apagado ou de outro projeto não derruba a lista inteira.
            errorPolicy = "omit"
        };
        var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await CreateClient().PostAsync(url, body, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Azure DevOps (workitemsbatch) respondeu {(int)response.StatusCode}: {content[..Math.Min(content.Length, 500)]}");

        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<AzureCardSummaryResponse>();
        foreach (var item in items.EnumerateArray())
        {
            // errorPolicy=omit devolve null no lugar do card que falhou.
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("fields", out var fields)) continue;
            var workItem = JsonSerializer.Deserialize<AzureWorkItem>(item.GetRawText());
            if (workItem is null) continue;
            var f = workItem.Fields;
            result.Add(new AzureCardSummaryResponse
            {
                Id = workItem.Id,
                Title = f.Title,
                State = f.State,
                BoardColumn = f.BoardColumn,
                WorkItemType = f.WorkItemType,
                AssignedTo = f.AssignedTo?.DisplayName,
                AssignedToImageUrl = f.AssignedTo?.ImageUrl,
                ChangedDate = f.ChangedDate,
                Url = GetWorkItemBrowserUrl(workItem.Id.ToString()),
            });
        }
        return result;
    }

    public async Task<AzureWorkItem?> GetCardAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);

        var baseUrl = GetAzureBaseUrl();
        var apiVersion =  Config.GetConfigurationValue("ApiVersion");
        var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

        var query = $"";
        var fields = $"";
        
        url += query + fields;

        var client = CreateClient();
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
        await EnsureConfigAsync(cancellationToken);

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
        var apiVersion =  Config.GetConfigurationValue("ApiVersion");
        var url = $"{baseUrl}/wit/workitems/{id}?api-version={apiVersion}";

        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };
        
        var rootCauseFieldPath =  Config.GetConfigurationValue("RootCauseFieldPath");

        var patchData = new[] { new { op = "add", path = rootCauseFieldPath, value = rootCauseText } };
        var json = JsonSerializer.Serialize(patchData, jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        var client = CreateClient();
        var response = await client.PatchAsync(url, content, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
            return null;

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<AzureWorkItem>(responseContent);
    }

    // Mesma versão usada pela leitura dos comentários (a API de comments ainda é preview).
    private const string CommentsApiVersion = "7.1-preview.4";

    private static readonly JsonSerializerOptions PatchJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public async Task<long> PatchFieldsAsync(string id, IReadOnlyDictionary<string, object> fields, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);

        var apiVersion = Config.GetConfigurationValue("ApiVersion");
        if (string.IsNullOrWhiteSpace(apiVersion))
            apiVersion = "7.0";
        var url = $"{GetAzureBaseUrl()}/wit/workitems/{Uri.EscapeDataString(id)}?api-version={apiVersion}";

        var patch = fields.Select(f => new { op = "add", path = $"/fields/{f.Key}", value = f.Value }).ToArray();
        var content = new StringContent(JsonSerializer.Serialize(patch, PatchJsonOptions), Encoding.UTF8, "application/json-patch+json");

        var response = await CreateClient().PatchAsync(url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw DevOpsActionException.Upstream($"O DevOps recusou a alteração: {ExtractDevOpsError(body)}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("rev", out var rev) && rev.TryGetInt64(out var value) ? value : 0;
    }

    public async Task<IReadOnlyList<string>> GetWorkItemTypeStatesAsync(string workItemType, CancellationToken cancellationToken = default) =>
        (await GetWorkItemTypeStateInfosAsync(workItemType, cancellationToken)).Select(s => s.Name).ToList();

    /// <summary>Estados por organização/projeto/tipo — mudam raramente (processo do DevOps); a tela pede a cada abertura.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, IReadOnlyList<WorkItemTypeState> States)> StatesCache = new();
    private static readonly TimeSpan StatesCacheFor = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<WorkItemTypeState>> GetWorkItemTypeStateInfosAsync(string workItemType, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var baseUrl = GetAzureBaseUrl();
        var key = $"{baseUrl}|{workItemType}".ToLowerInvariant();
        if (StatesCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < StatesCacheFor)
            return hit.States;

        var configured = Config.GetConfigurationValue("ApiVersion");
        var client = CreateClient();
        // A API de estados saiu do preview na 7.1: tenta a versão configurada e, recusada, a 7.1-preview.1.
        foreach (var version in new[] { string.IsNullOrWhiteSpace(configured) ? "7.1" : configured, "7.1-preview.1" }.Distinct())
        {
            var url = $"{baseUrl}/wit/workitemtypes/{Uri.EscapeDataString(workItemType)}/states?api-version={version}";
            var response = await client.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest) continue;
            if (!response.IsSuccessStatusCode)
                throw DevOpsActionException.Upstream($"O DevOps não listou os estados de {workItemType}: {ExtractDevOpsError(body)}");
            using var doc = JsonDocument.Parse(body);
            IReadOnlyList<WorkItemTypeState> states = doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                    .Select(v => new WorkItemTypeState
                    {
                        Name = StringProp(v, "name") ?? string.Empty,
                        Category = StringProp(v, "category"),
                        Color = StringProp(v, "color")
                    })
                    .Where(s => s.Name.Length > 0)
                    .ToList()
                : [];
            StatesCache[key] = (DateTime.UtcNow, states);
            return states;
        }
        throw DevOpsActionException.Upstream($"O DevOps não listou os estados de {workItemType}.");
    }

    /// <summary>Picklists por organização/projeto/tipo/campo — mudam raramente (processo do DevOps).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, IReadOnlyList<string>? Values)> AllowedValuesCache = new();

    public async Task<IReadOnlyList<string>?> GetFieldAllowedValuesAsync(string workItemType, string field, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var baseUrl = GetAzureBaseUrl();
        var key = $"{baseUrl}|{workItemType}|{field}".ToLowerInvariant();
        if (AllowedValuesCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < StatesCacheFor)
            return hit.Values;

        var configured = Config.GetConfigurationValue("ApiVersion");
        var version = string.IsNullOrWhiteSpace(configured) ? "7.1" : configured;
        var url = $"{baseUrl}/wit/workitemtypes/{Uri.EscapeDataString(workItemType)}/fields/{Uri.EscapeDataString(field)}?$expand=allowedValues&api-version={version}";
        var response = await CreateClient().GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        IReadOnlyList<string>? values;
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            values = null; // o tipo não tem o campo neste processo
        else if (!response.IsSuccessStatusCode)
            throw DevOpsActionException.Upstream($"O DevOps não listou os valores de {field} em {workItemType}: {ExtractDevOpsError(body)}");
        else
        {
            using var doc = JsonDocument.Parse(body);
            values = doc.RootElement.TryGetProperty("allowedValues", out var allowed) && allowed.ValueKind == JsonValueKind.Array
                ? allowed.EnumerateArray()
                    .Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => v.GetString()!.Trim())
                    .Where(v => v.Length > 0)
                    .ToList()
                : [];
        }
        AllowedValuesCache[key] = (DateTime.UtcNow, values);
        return values;
    }

    private static string? StringProp(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public async Task<int> UpsertCommentAsync(string id, string html, int? commentId, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);

        var baseUrl = $"{GetAzureBaseUrl()}/wit/workItems/{Uri.EscapeDataString(id)}/comments";
        var client = CreateClient();

        HttpResponseMessage response;
        if (commentId is { } existing)
        {
            response = await client.PatchAsync($"{baseUrl}/{existing}?api-version={CommentsApiVersion}", CommentBody(html), cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                response = await client.PostAsync($"{baseUrl}?api-version={CommentsApiVersion}", CommentBody(html), cancellationToken);
        }
        else
        {
            response = await client.PostAsync($"{baseUrl}?api-version={CommentsApiVersion}", CommentBody(html), cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw DevOpsActionException.Upstream($"Não foi possível publicar o comentário na discussion: {ExtractDevOpsError(body)}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var value)
            ? value
            : throw DevOpsActionException.Upstream("O DevOps não devolveu o id do comentário");
    }

    public async Task<string> UploadAttachmentAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);

        var url = $"{GetAzureBaseUrl()}/wit/attachments?fileName={Uri.EscapeDataString(fileName)}&uploadType=Simple&api-version=7.0";
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var response = await CreateClient().PostAsync(url, body, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw DevOpsActionException.Upstream($"Não foi possível anexar {fileName} no DevOps: {ExtractDevOpsError(text)}");

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String && u.GetString() is { Length: > 0 } link
            ? link
            : throw DevOpsActionException.Upstream("O DevOps não devolveu a URL do anexo");
    }

    private static StringContent CommentBody(string html) =>
        new(JsonSerializer.Serialize(new { text = html }, PatchJsonOptions), Encoding.UTF8, "application/json");

    /// <summary>Mensagem legível do erro do DevOps ({ "message": ... }), ou o corpo cortado.</summary>
    private static string ExtractDevOpsError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                return m.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            // Corpo não é JSON: devolve o texto.
        }
        return body.Length > 300 ? body[..300] : body;
    }

    public async Task<AzureCardFullResponse?> GetCardFullAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);

        var baseUrl = GetAzureBaseUrl();
        var apiVersion = Config.GetConfigurationValue("ApiVersion");
        if (string.IsNullOrWhiteSpace(apiVersion))
            apiVersion = "7.0";

        var client = CreateClient();

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
        var names = FieldNames;
        return new AzureCardAlerts
        {
            MissingRootCause = IsEmpty(fields, names.RootCause),
            MissingResolutionType = IsEmpty(fields, names.ResolutionType),
            MissingGeneralClassification = IsEmpty(fields, names.GeneralClassification),
            MissingClassification = IsEmpty(fields, names.Classification),
            // Bug Type só existe no Bug (User Story não tem o campo).
            MissingBugType = IsBug(fields) && IsEmpty(fields, names.BugType),
            RemainingNotZero = RemainingNotZero(fields, names.RemainingWork)
        };
    }

    private static bool IsBug(Dictionary<string, JsonElement> fields) =>
        fields.TryGetValue("System.WorkItemType", out var el) && el.ValueKind == JsonValueKind.String
        && string.Equals(el.GetString(), "Bug", StringComparison.OrdinalIgnoreCase);

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

    public async Task<IReadOnlyList<AzureCardEvidence>> GetCardEvidenceAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var baseUrl = GetAzureBaseUrl();
        var apiVersion = Config.GetConfigurationValue("ApiVersion");
        if (string.IsNullOrWhiteSpace(apiVersion))
            apiVersion = "7.0";
        var client = CreateClient();

        var response = await client.GetAsync($"{baseUrl}/wit/workitems/{Uri.EscapeDataString(id)}?$expand=all&api-version={apiVersion}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new DevOpsActionException(502, $"O DevOps recusou a leitura do card {id} (HTTP {(int)response.StatusCode}).");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        var comments = new List<AzureCardComment>();
        var cResp = await client.GetAsync($"{baseUrl}/wit/workItems/{Uri.EscapeDataString(id)}/comments?api-version=7.1-preview.4", cancellationToken);
        if (cResp.IsSuccessStatusCode)
        {
            using var cDoc = JsonDocument.Parse(await cResp.Content.ReadAsStringAsync(cancellationToken));
            if (cDoc.RootElement.TryGetProperty("comments", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var c in arr.EnumerateArray())
                    comments.Add(new AzureCardComment
                    {
                        Id = c.TryGetProperty("id", out var ci) && ci.TryGetInt64(out var civ) ? civ : 0,
                        Text = c.TryGetProperty("text", out var ct) ? ct.GetString() : null,
                        CreatedByName = GetIdentityName(c, "createdBy"),
                        CreatedDate = GetDate(c, "createdDate"),
                    });
        }
        return AzureCardEvidenceParser.Parse(doc.RootElement, comments);
    }

    public async Task<AzureAttachmentDownload> DownloadAttachmentAsync(string url, long maxBytes, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var organization = Config.GetConfigurationValue("Organization") ?? string.Empty;
        if (!AzureCardEvidenceParser.IsAllowedDownload(url, organization))
            throw new DevOpsActionException(400, "Anexo fora do DevOps da organização configurada — não baixado.");
        var target = url.Contains("api-version=", StringComparison.OrdinalIgnoreCase) ? url : url + (url.Contains('?') ? "&" : "?") + "api-version=7.0";
        if (!target.Contains("download=", StringComparison.OrdinalIgnoreCase)) target += "&download=true";

        var client = CreateClient();
        using var response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new DevOpsActionException(502, $"O DevOps recusou o anexo (HTTP {(int)response.StatusCode}).");
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var declared = response.Content.Headers.ContentLength;
        if (declared > maxBytes)
            return new AzureAttachmentDownload([], contentType, true, declared);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes)
                return new AzureAttachmentDownload([], contentType, true, null);
        }
        return new AzureAttachmentDownload(buffer.ToArray(), contentType, false, buffer.Length);
    }

    private static string? StripHtml(string? s)
        => string.IsNullOrEmpty(s)
            ? s
            : System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", string.Empty).Trim();

    private string GetAzureBaseUrl()
    {
        var organization =  Config.GetConfigurationValue("Organization");
        var project =  Config.GetConfigurationValue("Project");
        if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(project))
            throw new InvalidOperationException("Configurações do Azure DevOps não encontradas");
        var encodedProject = Uri.EscapeDataString(project);
        return $"https://dev.azure.com/{organization}/{encodedProject}/_apis";
    }

    private string GetWorkItemBrowserUrl(string id)
    {
        var organization = Config.GetConfigurationValue("Organization");
        var project = Config.GetConfigurationValue("Project");
        var encodedProject = Uri.EscapeDataString(project);
        return $"https://dev.azure.com/{organization}/{encodedProject}/_workitems/edit/{id}";
    }
    
}


