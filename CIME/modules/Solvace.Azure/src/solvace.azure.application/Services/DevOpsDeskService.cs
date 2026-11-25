using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using solvace.azure.application.Contract;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;
using Microsoft.Extensions.Logging;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;

namespace solvace.azure.application.Services;

/// <summary>
/// 0099 (Mesa de gestão): o DevOps pela integração de quem age. Mesma conexão do <see cref="AzureService"/> (plugin
/// "AzureDevOps Configurations" resolvido por requisição, PAT no header); o mapeamento do JSON fica em
/// <see cref="DevOpsDeskMapping"/>. Caches estáticos por organização/projeto: board 60 s, metadados 5–10 min, tags 60 s; pessoas
/// (ajustes 1): membros dos times 10 min, usuários da organização 1 h, pessoas de cards recentes 30 min.
/// </summary>
public class DevOpsDeskService(IHttpClientFactory httpClientFactory, IPluginConfigurationResolver configurationResolver,
    ILogger<DevOpsDeskService> logger) : IDevOpsDeskService
{
    private const string PluginName = "AzureDevOps Configurations";
    private const string Api = "7.1";
    private const string TagsApi = "7.1-preview.1";
    private const string CommentsApi = "7.1-preview.4";
    private const string GraphApi = "7.1-preview.1";
    private const int BatchSize = 200;

    private static readonly TimeSpan BoardCacheFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MetaCacheFor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PeopleCacheFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TagsCacheFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan OrgUsersCacheFor = TimeSpan.FromHours(1);
    private static readonly TimeSpan RecentPeopleCacheFor = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IdentitySearchCacheFor = TimeSpan.FromMinutes(5);
    /// <summary>Fonte de pessoas que falhou (PAT sem escopo, API fora): fica de fora por este tempo, sem tentar a cada tecla.</summary>
    private static readonly TimeSpan FailedSourceCacheFor = TimeSpan.FromMinutes(10);
    private const int MaxGraphPages = 40;
    private const int RecentPeopleCards = 2000;
    /// <summary>Fontes de pessoas que já logaram a falha (uma vez por organização/fonte).</summary>
    private static readonly ConcurrentDictionary<string, byte> LoggedPeopleFailures = new();
    private static readonly ConcurrentDictionary<string, (DateTime At, object Value)> Cache = new();

    private static readonly JsonSerializerOptions BodyJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private PluginConfiguration? _config;

    // ---------------------------------------------------------------- conexão

    private async Task EnsureConfigAsync(CancellationToken ct)
    {
        _config ??= await configurationResolver.GetEffectiveConfigurationAsync(PluginName, ct);
        if (string.IsNullOrWhiteSpace(_config.GetConfigurationValue("Organization")) || string.IsNullOrWhiteSpace(_config.GetConfigurationValue("Project")))
            throw new DevOpsDeskException(409, "not_configured", "Organização/projeto do Azure DevOps não configurados em \"Minhas integrações\" (AzureDevOps Configurations).");
    }

    private PluginConfiguration Config => _config ?? throw new InvalidOperationException("Configuração do Azure DevOps não resolvida");

    private string Organization => Config.GetConfigurationValue("Organization")!.Trim();
    private string Project => Config.GetConfigurationValue("Project")!.Trim();
    private string OrganizationUrl => $"https://dev.azure.com/{Organization}";
    /// <summary>Serviço de identidades/usuários da organização (Graph, identities) — mesma autenticação (Basic com o PAT).</summary>
    private string VsspsUrl => $"https://vssps.dev.azure.com/{Organization}";
    private string ProjectApi => $"{OrganizationUrl}/{Uri.EscapeDataString(Project)}/_apis";
    private string TeamApi(string team) => $"{OrganizationUrl}/{Uri.EscapeDataString(Project)}/{Uri.EscapeDataString(team)}/_apis";
    private string BrowserUrl(int id) => $"{OrganizationUrl}/{Uri.EscapeDataString(Project)}/_workitems/edit/{id}";
    private string CacheKey(string what) => $"{Organization}|{Project}|{what}".ToLowerInvariant();
    /// <summary>Cache da organização inteira (usuários, identidades) — vale para todos os projetos dela.</summary>
    private string OrgCacheKey(string what) => $"{Organization}|*|{what}".ToLowerInvariant();

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("AzureDevOps");
        var pat = Config.GetConfigurationValue("PersonalAccessToken");
        if (!string.IsNullOrEmpty(pat))
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}")));
        return client;
    }

    /// <summary>Chamada ao DevOps: erro vira <see cref="DevOpsDeskException"/> com a mensagem dele (<paramref name="what"/> na frente).</summary>
    private async Task<JsonDocument> SendAsync(HttpMethod method, string url, object? body, string what, CancellationToken ct,
        string contentType = "application/json") =>
        (await SendFullAsync(method, url, body, what, ct, contentType)).Doc;

    /// <summary>Como <see cref="SendAsync"/>, com o <c>x-ms-continuationtoken</c> da resposta (listas paginadas do Graph).</summary>
    private async Task<(JsonDocument Doc, string? Continuation)> SendFullAsync(HttpMethod method, string url, object? body, string what,
        CancellationToken ct, string contentType = "application/json")
    {
        var (response, text) = await SendOnceAsync(method, url, body, contentType, ct);
        // Organização que ainda não tem a 7.1 GA de alguma API: tenta a preview (como os estados do tipo no AzureService).
        var gaVersion = url.Contains($"api-version={Api}&", StringComparison.Ordinal) || url.EndsWith($"api-version={Api}", StringComparison.Ordinal);
        if (response.StatusCode == HttpStatusCode.BadRequest && gaVersion && text.Contains("VssVersionOutOfRange", StringComparison.OrdinalIgnoreCase))
        {
            response.Dispose();
            (response, text) = await SendOnceAsync(method, url.Replace($"api-version={Api}", $"api-version={Api}-preview"), body, contentType, ct);
        }
        using var _ = response;
        // PAT inválido/vencido: o DevOps responde 203 com a página de login (HTML).
        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation
            || (response.IsSuccessStatusCode && text.TrimStart().StartsWith('<')))
            throw new DevOpsDeskException(403, "devops_forbidden",
                "O DevOps não aceitou o token (PAT) da sua integração — confira em \"Minhas integrações\" (AzureDevOps Configurations).");
        if (!response.IsSuccessStatusCode)
            throw DevOpsDeskException.FromDevOps((int)response.StatusCode, $"{what}: {ExtractDevOpsError(text)}");
        var continuation = response.Headers.TryGetValues("x-ms-continuationtoken", out var tokens) ? tokens.FirstOrDefault() : null;
        return (JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text), string.IsNullOrWhiteSpace(continuation) ? null : continuation);
    }

    private async Task<(HttpResponseMessage Response, string Text)> SendOnceAsync(HttpMethod method, string url, object? body, string contentType,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, BodyJson), Encoding.UTF8, contentType);
        var response = await CreateClient().SendAsync(request, ct);
        return (response, await response.Content.ReadAsStringAsync(ct));
    }

    private Task<JsonDocument> GetAsync(string url, string what, CancellationToken ct) => SendAsync(HttpMethod.Get, url, null, what, ct);

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

    private static async Task<T> CachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory) where T : class
    {
        if (Cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < ttl && hit.Value is T value)
            return value;
        var fresh = await factory();
        Cache[key] = (DateTime.UtcNow, fresh);
        return fresh;
    }

    // ---------------------------------------------------------------- metadados

    public async Task<IReadOnlyList<DevOpsTypeInfo>> GetTypesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        return await CachedAsync<IReadOnlyList<DevOpsTypeInfo>>(CacheKey("types"), MetaCacheFor, async () =>
        {
            using var doc = await GetAsync($"{ProjectApi}/wit/workitemtypes?api-version={Api}", "O DevOps não listou os tipos", cancellationToken);
            return DevOpsDeskMapping.ParseTypes(doc.RootElement);
        });
    }

    public async Task<IReadOnlyList<DevOpsFieldInfo>> GetFieldsAsync(string workItemType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workItemType))
            throw DevOpsDeskException.Invalid("Informe o tipo (type).");
        await EnsureConfigAsync(cancellationToken);
        var type = workItemType.Trim();
        var project = await CachedAsync<Dictionary<string, DevOpsDeskMapping.ProjectField>>(CacheKey("fields"), PeopleCacheFor, async () =>
        {
            using var doc = await GetAsync($"{ProjectApi}/wit/fields?api-version={Api}", "O DevOps não listou os campos", cancellationToken);
            return DevOpsDeskMapping.ParseProjectFields(doc.RootElement);
        });
        return await CachedAsync<IReadOnlyList<DevOpsFieldInfo>>(CacheKey($"typefields|{type}"), MetaCacheFor, async () =>
        {
            using var doc = await GetAsync($"{ProjectApi}/wit/workitemtypes/{Uri.EscapeDataString(type)}/fields?$expand=allowedValues&api-version={Api}",
                $"O DevOps não listou os campos de {type}", cancellationToken);
            return DevOpsDeskMapping.ParseTypeFields(doc.RootElement, project);
        });
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(string? q, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var all = await CachedAsync<IReadOnlyList<string>>(CacheKey("tags"), TagsCacheFor, async () =>
        {
            using var doc = await GetAsync($"{ProjectApi}/wit/tags?api-version={TagsApi}", "O DevOps não listou as tags", cancellationToken);
            return DevOpsDeskMapping.ParseTags(doc.RootElement, null);
        });
        var term = q?.Trim();
        return string.IsNullOrEmpty(term) ? all : all.Where(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public Task<IReadOnlyList<DevOpsNodeInfo>> GetAreasAsync(CancellationToken cancellationToken = default) => NodesAsync("area", cancellationToken);

    public Task<IReadOnlyList<DevOpsNodeInfo>> GetIterationsAsync(CancellationToken cancellationToken = default) => NodesAsync("iteration", cancellationToken);

    private async Task<IReadOnlyList<DevOpsNodeInfo>> NodesAsync(string structureType, CancellationToken ct)
    {
        await EnsureConfigAsync(ct);
        return await CachedAsync<IReadOnlyList<DevOpsNodeInfo>>(CacheKey($"nodes|{structureType}"), MetaCacheFor, async () =>
        {
            using var doc = await GetAsync($"{ProjectApi}/wit/classificationnodes?$depth=10&api-version={Api}",
                "O DevOps não listou áreas/iterações", ct);
            return DevOpsDeskMapping.FlattenNodes(doc.RootElement, structureType);
        });
    }

    public async Task<IReadOnlyList<DevOpsNamedRef>> GetTeamsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        return await CachedAsync<IReadOnlyList<DevOpsNamedRef>>(CacheKey("teams"), MetaCacheFor, async () =>
        {
            using var doc = await GetAsync($"{OrganizationUrl}/_apis/projects/{Uri.EscapeDataString(Project)}/teams?$top=500&api-version={Api}",
                "O DevOps não listou os times", cancellationToken);
            return DevOpsDeskMapping.ParseNamed(doc.RootElement).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    public async Task<IReadOnlyList<DevOpsPersonInfo>> GetPeopleAsync(string? q, int? top = null, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var term = q?.Trim() ?? string.Empty;
        // Ajustes 1 da 0099: os times são a base (erro aqui é erro); as outras fontes são melhor esforço (PAT sem escopo → fora).
        var teams = TeamMembersAsync(cancellationToken);
        var org = BestEffortPeopleAsync("graph", OrgCacheKey("people|graph"), OrgUsersCacheFor, OrgUsersAsync, cancellationToken);
        var recent = BestEffortPeopleAsync("recent", CacheKey("people|recent"), RecentPeopleCacheFor, RecentCardPeopleAsync, cancellationToken);
        var search = DevOpsDeskMapping.Fold(term).Length >= 2
            ? BestEffortPeopleAsync("identities", OrgCacheKey($"people|identities|{DevOpsDeskMapping.Fold(term)}"), IdentitySearchCacheFor,
                ct => SearchIdentitiesAsync(term, ct), cancellationToken)
            : Task.FromResult<IReadOnlyList<DevOpsPersonInfo>>([]);
        await Task.WhenAll(teams, org, recent, search);
        var people = DevOpsDeskMapping.MergePeople(await teams, await org, await search, await recent);
        return DevOpsDeskMapping.FilterPeople(people, term, top);
    }

    /// <summary>Membros de todos os times do projeto (cache de 10 min) — a fonte que sempre existiu.</summary>
    private Task<IReadOnlyList<DevOpsPersonInfo>> TeamMembersAsync(CancellationToken cancellationToken) =>
        CachedAsync<IReadOnlyList<DevOpsPersonInfo>>(CacheKey("people"), PeopleCacheFor, async () =>
        {
            var teams = await GetTeamsAsync(cancellationToken);
            var found = new ConcurrentBag<DevOpsPersonInfo>();
            using var gate = new SemaphoreSlim(6);
            await Task.WhenAll(teams.Select(async team =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    using var doc = await GetAsync(
                        $"{OrganizationUrl}/_apis/projects/{Uri.EscapeDataString(Project)}/teams/{Uri.EscapeDataString(team.Id.Length > 0 ? team.Id : team.Name)}/members?$top=1000&api-version={Api}",
                        $"O DevOps não listou os membros de {team.Name}", cancellationToken);
                    foreach (var p in DevOpsDeskMapping.ParseMembers(doc.RootElement))
                        found.Add(p);
                }
                catch (DevOpsDeskException e) when (e.StatusCode is 403 or 404)
                {
                    // Time sem acesso para quem pede: segue com os outros.
                }
                finally
                {
                    gate.Release();
                }
            }));
            return DevOpsDeskMapping.MergePeople(found);
        });

    /// <summary>
    /// Fonte de pessoas que não pode derrubar a lista: falha (PAT sem o escopo, API fora, tempo) vira lista vazia, guardada por
    /// <see cref="FailedSourceCacheFor"/> e logada uma vez por organização/fonte.
    /// </summary>
    private async Task<IReadOnlyList<DevOpsPersonInfo>> BestEffortPeopleAsync(string source, string key, TimeSpan ttl,
        Func<CancellationToken, Task<IReadOnlyList<DevOpsPersonInfo>>> load, CancellationToken ct)
    {
        if (Cache.TryGetValue(key, out var hit) && hit.Value is IReadOnlyList<DevOpsPersonInfo> cached
            && DateTime.UtcNow - hit.At < (cached.Count == 0 ? FailedSourceCacheFor : ttl))
            return cached;
        try
        {
            var fresh = await load(ct);
            Cache[key] = (DateTime.UtcNow, fresh);
            return fresh;
        }
        catch (Exception e) when (!ct.IsCancellationRequested && e is DevOpsActionException or HttpRequestException or JsonException or OperationCanceledException)
        {
            if (LoggedPeopleFailures.TryAdd($"{Organization}|{source}".ToLowerInvariant(), 0))
                logger.LogWarning("Mesa de gestão: pessoas de \"{Source}\" indisponíveis na organização {Organization} (segue sem elas): {Error}",
                    source, Organization, e.Message);
            Cache[key] = (DateTime.UtcNow, (IReadOnlyList<DevOpsPersonInfo>)[]);
            return [];
        }
    }

    /// <summary>Usuários da organização pelo Graph (<c>subjectTypes=aad,msa</c>), paginados pelo <c>x-ms-continuationtoken</c>.</summary>
    private async Task<IReadOnlyList<DevOpsPersonInfo>> OrgUsersAsync(CancellationToken ct)
    {
        var people = new List<DevOpsPersonInfo>();
        string? continuation = null;
        for (var page = 0; page < MaxGraphPages; page++)
        {
            var url = $"{VsspsUrl}/_apis/graph/users?subjectTypes=aad,msa&api-version={GraphApi}"
                      + (continuation is null ? string.Empty : $"&continuationToken={Uri.EscapeDataString(continuation)}");
            var (doc, next) = await SendFullAsync(HttpMethod.Get, url, null, "O DevOps não listou os usuários da organização", ct);
            using (doc)
                people.AddRange(DevOpsDeskMapping.ParseGraphUsers(doc.RootElement));
            if (next is null || next == continuation) break;
            continuation = next;
        }
        return DevOpsDeskMapping.MergePeople(people);
    }

    /// <summary>Busca de identidades da organização pelo termo (nome, e-mail ou login).</summary>
    private async Task<IReadOnlyList<DevOpsPersonInfo>> SearchIdentitiesAsync(string term, CancellationToken ct)
    {
        using var doc = await GetAsync(
            $"{VsspsUrl}/_apis/identities?searchFilter=General&filterValue={Uri.EscapeDataString(term)}&queryMembership=None&api-version={Api}",
            "O DevOps não buscou as identidades", ct);
        return DevOpsDeskMapping.ParseIdentities(doc.RootElement, OrganizationUrl);
    }

    /// <summary>Quem aparece nos cards alterados nos últimos 180 dias (até 2000 cards): responsável, quem criou e quem alterou.</summary>
    private async Task<IReadOnlyList<DevOpsPersonInfo>> RecentCardPeopleAsync(CancellationToken ct)
    {
        var ids = await WiqlAsync(DevOpsDeskMapping.RecentPeopleWiql, RecentPeopleCards, ct);
        var items = await BatchAsync(ids, DevOpsDeskMapping.RecentPeopleFields, ct);
        return DevOpsDeskMapping.MergePeople(DevOpsDeskMapping.PeopleFromWorkItems(items));
    }

    /// <summary>Time padrão do projeto (<c>defaultTeam</c>).</summary>
    private async Task<string> DefaultTeamAsync(CancellationToken ct)
    {
        var team = await CachedAsync<string>(CacheKey("defaultteam"), PeopleCacheFor, async () =>
        {
            using var doc = await GetAsync($"{OrganizationUrl}/_apis/projects/{Uri.EscapeDataString(Project)}?api-version={Api}",
                "O DevOps não devolveu o projeto", ct);
            return doc.RootElement.TryGetProperty("defaultTeam", out var dt) && dt.ValueKind == JsonValueKind.Object
                   && dt.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name
                ? name
                : string.Empty;
        });
        return team.Length > 0 ? team : throw DevOpsDeskException.Invalid("O projeto não tem time padrão no DevOps — informe o time (team).");
    }

    private async Task<string> TeamOrDefaultAsync(string? team, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(team) ? await DefaultTeamAsync(ct) : team.Trim();

    public async Task<IReadOnlyList<DevOpsNamedRef>> GetBoardsAsync(string? team, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var t = await TeamOrDefaultAsync(team, cancellationToken);
        return await CachedAsync<IReadOnlyList<DevOpsNamedRef>>(CacheKey($"boards|{t}"), MetaCacheFor, async () =>
        {
            using var doc = await GetAsync($"{TeamApi(t)}/work/boards?api-version={Api}", $"O DevOps não listou os boards de {t}", cancellationToken);
            return DevOpsDeskMapping.ParseNamed(doc.RootElement);
        });
    }

    /// <summary>Configuração do backlog do time: campo de ordem e o nome do backlog de requisitos (board padrão).</summary>
    private sealed record BacklogInfo(string? OrderField, string? RequirementBacklog);

    private async Task<BacklogInfo> BacklogAsync(string team, CancellationToken ct) =>
        await CachedAsync<BacklogInfo>(CacheKey($"backlog|{team}"), MetaCacheFor, async () =>
        {
            using var doc = await GetAsync($"{TeamApi(team)}/work/backlogconfiguration?api-version={Api}",
                $"O DevOps não devolveu a configuração do backlog de {team}", ct);
            var root = doc.RootElement;
            var requirement = root.TryGetProperty("requirementBacklog", out var rb) && rb.ValueKind == JsonValueKind.Object
                              && rb.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            return new BacklogInfo(DevOpsDeskMapping.ParseOrderField(root), requirement);
        });

    public async Task<DevOpsBoardConfig> GetBoardAsync(string? team, string? board, string? workItemType = null, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var t = await TeamOrDefaultAsync(team, cancellationToken);
        if (!string.IsNullOrWhiteSpace(board))
            return await LoadBoardAsync(t, board.Trim(), cancellationToken);

        var boards = await GetBoardsAsync(t, cancellationToken);
        if (boards.Count == 0)
            throw DevOpsDeskException.NotFoundDesk($"O time {t} não tem board no DevOps.");
        var backlog = await BacklogAsync(t, cancellationToken);
        var requirement = boards.FirstOrDefault(b => string.Equals(b.Name, backlog.RequirementBacklog, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(workItemType))
        {
            // O board do backlog de requisitos primeiro (é o que o time mais usa), depois os outros.
            foreach (var candidate in boards.OrderByDescending(b => b == requirement))
            {
                var config = await LoadBoardAsync(t, candidate.Id.Length > 0 ? candidate.Id : candidate.Name, cancellationToken);
                if (config.Types.Any(x => x.Name.Equals(workItemType.Trim(), StringComparison.OrdinalIgnoreCase)))
                    return config;
            }
            throw DevOpsDeskException.NoMapping($"Nenhum board do time {t} mostra {workItemType} — informe o time/board.");
        }
        var chosen = requirement ?? boards[0];
        return await LoadBoardAsync(t, chosen.Id.Length > 0 ? chosen.Id : chosen.Name, cancellationToken);
    }

    private async Task<DevOpsBoardConfig> LoadBoardAsync(string team, string board, CancellationToken ct) =>
        await CachedAsync<DevOpsBoardConfig>(CacheKey($"board|{team}|{board}"), BoardCacheFor, async () =>
        {
            var types = await GetTypesAsync(ct);
            var colors = types.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Color, StringComparer.OrdinalIgnoreCase);
            var backlog = await BacklogAsync(team, ct);
            using var doc = await GetAsync($"{TeamApi(team)}/work/boards/{Uri.EscapeDataString(board)}?api-version={Api}",
                $"O DevOps não devolveu o board {board} de {team}", ct);
            return DevOpsDeskMapping.ParseBoard(doc.RootElement, team, colors, backlog.OrderField);
        });

    // ---------------------------------------------------------------- board e consultas

    public async Task<DevOpsBoardItemsResponse> GetBoardItemsAsync(DevOpsBoardItemsRequest request, IReadOnlyCollection<int>? onlyIds,
        DevOpsBoardFilters? epicFilters = null, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var config = await GetBoardAsync(request.Team, request.Board, null, cancellationToken);
        var top = Math.Clamp(request.Top ?? 300, 1, 1000);
        var saved = DevOpsDeskMapping.NormalizeBoardFilters(epicFilters);
        // Filtros salvos do épico: tipos/estados também na WIQL (o top não corta antes do filtro); o resto (lane, responsável,
        // tags, texto sem acento) no fim, igual à tela.
        var wiqlFilters = saved.IsEmpty ? request : WithSavedFilters(request, saved);
        var ids = new List<int>();

        if (onlyIds is not null)
        {
            var wiql = DevOpsDeskMapping.BuildBoardWiql(config, null, [], onlyIds.Take(1000).ToList(), null, wiqlFilters);
            if (wiql is not null)
                ids.AddRange(await WiqlAsync(wiql, top, cancellationToken));
        }
        else
        {
            var (field, values) = await TeamFieldValuesAsync(config.Team, cancellationToken);
            // Colunas de trabalho inteiras (na ordem do backlog); a coluna final só com os mais recentes, como no DevOps.
            if (DevOpsDeskMapping.BuildBoardWiql(config, field, values, null, false, wiqlFilters) is { } open)
                ids.AddRange(await WiqlAsync(open, top, cancellationToken));
            if (DevOpsDeskMapping.BuildBoardWiql(config, field, values, null, true, wiqlFilters) is { } closed)
                ids.AddRange(await WiqlAsync(closed, Math.Min(top, 50), cancellationToken));
        }

        var items = await BatchAsync(ids.Distinct().ToList(), DevOpsDeskMapping.BoardItemFields(config), cancellationToken);
        var cards = items.Select(i => DevOpsDeskMapping.ParseBoardItem(i, config)).ToList();
        if (saved.IsEmpty)
            return new DevOpsBoardItemsResponse(config.ConfigHash, cards);
        return new DevOpsBoardItemsResponse(config.ConfigHash, DevOpsDeskMapping.ApplyBoardFilters(cards, saved, config), saved);
    }

    /// <summary>Pedido com os tipos/estados salvos no épico quando a tela não mandou os seus (os dela continuam valendo).</summary>
    private static DevOpsBoardItemsRequest WithSavedFilters(DevOpsBoardItemsRequest request, DevOpsBoardFilters saved) => new()
    {
        Team = request.Team, Board = request.Board, Epic = request.Epic, Scope = request.Scope, Ids = request.Ids, Text = request.Text,
        AssignedTo = request.AssignedTo, Tags = request.Tags, Top = request.Top, UseEpicFilters = request.UseEpicFilters,
        Types = request.Types is { Count: > 0 } || saved.Types.Count == 0 ? request.Types : saved.Types,
        States = request.States is { Count: > 0 } || saved.States.Count == 0 ? request.States : saved.States
    };

    /// <summary>
    /// Card criado sem área num time: recebe a área padrão do time (como o "+ New" do board do DevOps) — sem isso ele cai na
    /// área raiz do projeto e não aparece no board. Time sem campo de time = área, ou área informada: não mexe.
    /// </summary>
    private async Task<Dictionary<string, JsonElement>?> WithTeamAreaAsync(Dictionary<string, JsonElement>? fields, string? team, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(team) || fields?.ContainsKey("System.AreaPath") == true) return fields;
        try
        {
            var (field, _, defaultValue) = await TeamFieldValuesFullAsync(team.Trim(), ct);
            if (string.IsNullOrWhiteSpace(defaultValue) || !string.Equals(field ?? "System.AreaPath", "System.AreaPath", StringComparison.OrdinalIgnoreCase))
                return fields;
            var withArea = fields is null ? new Dictionary<string, JsonElement>() : new Dictionary<string, JsonElement>(fields);
            withArea["System.AreaPath"] = JsonSerializer.SerializeToElement(defaultValue);
            return withArea;
        }
        catch (DevOpsActionException)
        {
            return fields;
        }
    }

    private async Task<(string? Field, IReadOnlyList<(string Value, bool IncludeChildren)> Values)> TeamFieldValuesAsync(string team, CancellationToken ct)
    {
        var (field, values, _) = await TeamFieldValuesFullAsync(team, ct);
        return (field, values);
    }

    private async Task<(string? Field, IReadOnlyList<(string Value, bool IncludeChildren)> Values, string? DefaultValue)> TeamFieldValuesFullAsync(string team, CancellationToken ct)
    {
        using var doc = await GetAsync($"{TeamApi(team)}/work/teamsettings/teamfieldvalues?api-version={Api}",
            $"O DevOps não devolveu as áreas do time {team}", ct);
        var root = doc.RootElement;
        var field = root.TryGetProperty("field", out var f) && f.ValueKind == JsonValueKind.Object && f.TryGetProperty("referenceName", out var r)
            ? r.GetString()
            : null;
        var values = root.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
                .Where(x => x.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
                .Select(x => (x.GetProperty("value").GetString()!, x.TryGetProperty("includeChildren", out var ic) && ic.ValueKind == JsonValueKind.True))
                .ToList()
            : [];
        var defaultValue = root.TryGetProperty("defaultValue", out var dv) && dv.ValueKind == JsonValueKind.String ? dv.GetString() : null;
        return (field, values, defaultValue);
    }

    private async Task<List<int>> WiqlAsync(string wiql, int top, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Post, $"{ProjectApi}/wit/wiql?$top={top}&api-version={Api}", new { query = wiql },
            "O DevOps recusou a consulta", ct);
        return DevOpsDeskMapping.ParseWiqlIds(doc.RootElement).Take(top).ToList();
    }

    /// <summary><c>workitemsbatch</c> em blocos de 200, na ordem dos ids; cards apagados/sem acesso ficam de fora.</summary>
    private async Task<List<JsonElement>> BatchAsync(IReadOnlyList<int> ids, IReadOnlyCollection<string> fields, CancellationToken ct)
    {
        var byId = new Dictionary<int, JsonElement>();
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            using var doc = await SendAsync(HttpMethod.Post, $"{ProjectApi}/wit/workitemsbatch?api-version={Api}",
                new { ids = chunk, fields, errorPolicy = "omit" }, "O DevOps recusou a leitura dos cards", ct);
            if (doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.TryGetInt32(out var idv))
                        byId[idv] = item.Clone();
        }
        return ids.Where(byId.ContainsKey).Select(i => byId[i]).ToList();
    }

    public async Task<DevOpsQueryResponse> QueryAsync(DevOpsQueryRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConfigAsync(cancellationToken);
        var top = Math.Clamp(request.Top ?? 500, 1, 2000);
        var wiql = string.IsNullOrWhiteSpace(request.Wiql) ? DevOpsDeskMapping.BuildFilterWiql(request.Filter) : request.Wiql.Trim();
        var ids = await WiqlAsync(wiql, top, cancellationToken);
        var fields = (request.Fields ?? []).Select(f => f.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (fields.Count == 0) fields = DevOpsDeskMapping.DefaultQueryFields.ToList();
        var items = await BatchAsync(ids, fields, cancellationToken);
        return new DevOpsQueryResponse(ids.Count, items.Select(i => new DevOpsQueryItem(
            i.TryGetProperty("id", out var id) && id.TryGetInt32(out var idv) ? idv : 0,
            DevOpsDeskMapping.CloneFields(i))).ToList());
    }

    // ---------------------------------------------------------------- cards

    public async Task<DevOpsCardDetail> GetCardAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw DevOpsDeskException.Invalid("Informe o id do card.");
        await EnsureConfigAsync(cancellationToken);
        using var doc = await GetAsync($"{ProjectApi}/wit/workitems/{id}?$expand=relations&api-version={Api}", $"O DevOps não devolveu o card {id}", cancellationToken);
        return DevOpsDeskMapping.ParseCard(doc.RootElement, BrowserUrl(id));
    }

    private async Task<DevOpsCardDetail> PatchCardAsync(int id, IReadOnlyList<DevOpsPatchOp> ops, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Patch, $"{ProjectApi}/wit/workitems/{id}?$expand=relations&api-version={Api}", ops,
            $"O DevOps recusou a alteração do card {id}", ct, "application/json-patch+json");
        return DevOpsDeskMapping.ParseCard(doc.RootElement, BrowserUrl(id));
    }

    public async Task<DevOpsCardWriteResult> CreateCardAsync(DevOpsCreateCardRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
            throw DevOpsDeskException.Invalid("Informe o tipo do card (type).");
        await EnsureConfigAsync(cancellationToken);
        var type = request.Type.Trim();
        var fields = await WithTeamAreaAsync(request.Fields, request.Team, cancellationToken);
        var ops = DevOpsDeskMapping.BuildCreatePatch(request.Title, fields, request.Tags, request.ParentId, OrganizationUrl);

        DevOpsCardDetail card;
        using (var doc = await SendAsync(HttpMethod.Post, $"{ProjectApi}/wit/workitems/${Uri.EscapeDataString(type)}?$expand=relations&api-version={Api}", ops,
                   $"O DevOps recusou a criação do {type}", cancellationToken, "application/json-patch+json"))
        {
            var created = doc.RootElement.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var v) ? v : 0;
            card = DevOpsDeskMapping.ParseCard(doc.RootElement, BrowserUrl(created));
        }

        var summary = new StringBuilder($"{card.Type} #{card.Id} criado: «{card.Title}»");
        if (request.ParentId is > 0) summary.Append($" (filho de #{request.ParentId})");
        if (card.Tags.Count > 0) summary.Append($"; tags {string.Join(", ", card.Tags)}");

        // Coluna/lane iniciais: segunda revisão (o DevOps cria no estado inicial do tipo; o movimento aplica o mapeamento).
        if (!string.IsNullOrWhiteSpace(request.Column) || !string.IsNullOrWhiteSpace(request.Lane))
        {
            try
            {
                var moved = await MoveCardAsync(card.Id, new DevOpsMoveCardRequest
                {
                    Team = request.Team, Board = request.Board, Lane = request.Lane,
                    Column = string.IsNullOrWhiteSpace(request.Column) ? string.Empty : request.Column
                }, cancellationToken, card);
                card = moved.Card;
                summary.Append("; ").Append(moved.Summary);
            }
            catch (DevOpsActionException e)
            {
                summary.Append($"; não foi para a coluna/lane pedida: {e.Message}");
            }
        }
        return new DevOpsCardWriteResult(card, summary.ToString());
    }

    public async Task<DevOpsCardWriteResult> UpdateCardAsync(int id, DevOpsUpdateCardRequest request, CancellationToken cancellationToken = default)
    {
        var current = await GetCardAsync(id, cancellationToken);
        var ops = DevOpsDeskMapping.BuildUpdatePatch(current.Fields, request.Fields, request.AddTags, request.RemoveTags);
        var comment = request.Comment?.Trim();
        if (ops.Count == 0 && string.IsNullOrEmpty(comment))
            throw DevOpsDeskException.Invalid("Nada para alterar no card (campos, tags ou comentário).");

        var card = ops.Count > 0 ? await PatchCardAsync(id, ops, cancellationToken) : current;
        var parts = new List<string>();
        var changed = ops.Where(o => o.Path.StartsWith("/fields/", StringComparison.Ordinal))
            .Select(o => o.Path["/fields/".Length..])
            .Where(f => !f.Equals(DevOpsDeskMapping.FieldTags, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (changed.Count > 0) parts.Add($"campos {string.Join(", ", changed)}");
        var added = card.Tags.Except(current.Tags, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = current.Tags.Except(card.Tags, StringComparer.OrdinalIgnoreCase).ToList();
        if (added.Count > 0 || removed.Count > 0)
            parts.Add("tags " + string.Join(" ", added.Select(t => $"+{t}").Concat(removed.Select(t => $"−{t}"))));
        if (!string.IsNullOrEmpty(comment))
        {
            await CommentAsync(id, comment, cancellationToken);
            parts.Add("comentário na discussion");
        }
        return new DevOpsCardWriteResult(card, $"{card.Type} #{id} editado: {string.Join("; ", parts)}");
    }

    public Task<DevOpsCardWriteResult> MoveCardAsync(int id, DevOpsMoveCardRequest request, CancellationToken cancellationToken = default) =>
        MoveCardAsync(id, request, cancellationToken, null);

    private async Task<DevOpsCardWriteResult> MoveCardAsync(int id, DevOpsMoveCardRequest request, CancellationToken ct, DevOpsCardDetail? known)
    {
        var current = known ?? await GetCardAsync(id, ct);
        var config = await GetBoardAsync(request.Team, request.Board, current.Type, ct);
        var column = string.IsNullOrWhiteSpace(request.Column)
            // Só a lane: fica na coluna em que o card já está (ou a do estado).
            ? DevOpsDeskMapping.CurrentColumn(current, config)
              ?? throw DevOpsDeskException.Invalid("Informe a coluna de destino.")
            : request.Column;
        var plan = DevOpsDeskMapping.BuildMove(config, current.Type, current.State, current.Fields, column, request.Lane, request.Done);

        var card = plan.Ops.Count > 0 ? await PatchCardAsync(id, plan.Ops, ct) : current;
        var summary = new StringBuilder($"{card.Type} #{id} movido para a coluna {plan.Column}");
        if (!string.Equals(current.State, plan.State, StringComparison.Ordinal)) summary.Append($" (estado {plan.State})");
        if (plan.Done) summary.Append(" — Done");
        if (plan.Lane is not null) summary.Append(plan.Lane.Length == 0 ? ", lane padrão" : $", lane {plan.Lane}");
        summary.Append($" no board {config.Board} ({config.Team})");

        if (request.PreviousId is > 0 || request.NextId is > 0)
        {
            try
            {
                using var _ = await SendAsync(HttpMethod.Patch, $"{TeamApi(config.Team)}/work/workitemsorder?api-version={Api}",
                    new { ids = new[] { id }, parentId = 0, previousId = request.PreviousId ?? 0, nextId = request.NextId ?? 0 },
                    $"O DevOps recusou a nova ordem do card {id}", ct);
                summary.Append(request.PreviousId is > 0 ? $", depois de #{request.PreviousId}" : $", antes de #{request.NextId}");
            }
            catch (DevOpsActionException) when (plan.Ops.Count > 0)
            {
                // A coluna já mudou: a ordem é enfeite — avisa no resumo em vez de falhar o movimento.
                summary.Append(", sem mudar a ordem (o DevOps recusou)");
            }
        }
        else if (plan.Ops.Count == 0)
            throw DevOpsDeskException.Invalid($"O card #{id} já está na coluna {plan.Column}{(plan.Lane is null ? string.Empty : $" / lane {plan.Lane}")}.");
        return new DevOpsCardWriteResult(card, summary.ToString(), plan.Ops.Count == 0 ? "reordered" : "moved");
    }

    public Task<int> CommentAsync(int id, string markdown, CancellationToken cancellationToken = default) =>
        CommentAsync(id, markdown, null, cancellationToken);

    public async Task<int> CommentAsync(int id, string markdown, IReadOnlyList<DevOpsMention>? mentions, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw DevOpsDeskException.Invalid("Informe o id do card.");
        if (string.IsNullOrWhiteSpace(markdown)) throw DevOpsDeskException.Invalid("Escreva o comentário.");
        await EnsureConfigAsync(cancellationToken);
        var people = await MentionTargetsAsync(mentions, cancellationToken);
        var html = DevOpsDeskMapping.LinkMentions(UserStorySpec.ToHtml(markdown), BrowserUrl, people);
        using var doc = await SendAsync(HttpMethod.Post, $"{ProjectApi}/wit/workItems/{id}/comments?api-version={CommentsApi}",
            new { text = html }, $"O DevOps recusou o comentário no card {id}", cancellationToken);
        return doc.RootElement.TryGetProperty("id", out var c) && c.TryGetInt32(out var v) ? v : 0;
    }

    /// <summary>GUID de cada pessoa mencionada: o <c>Id</c> quando já é GUID; senão a busca de identidades pelo login (melhor esforço).</summary>
    private async Task<List<DevOpsDeskMapping.MentionTarget>> MentionTargetsAsync(IReadOnlyList<DevOpsMention>? mentions, CancellationToken ct)
    {
        var result = new List<DevOpsDeskMapping.MentionTarget>();
        foreach (var m in mentions ?? [])
        {
            var name = m.DisplayName?.Trim() ?? string.Empty;
            if (name.Length == 0) continue;
            if (Guid.TryParse(m.Id, out var guid))
            {
                result.Add(new(name, guid.ToString()));
                continue;
            }
            var login = m.UniqueName?.Trim() ?? string.Empty;
            try
            {
                var found = await SearchIdentitiesAsync(login.Length > 0 ? login : name, ct);
                var match = login.Length > 0
                    ? found.FirstOrDefault(p => string.Equals(p.UniqueName, login, StringComparison.OrdinalIgnoreCase))
                    : found.FirstOrDefault(p => DevOpsDeskMapping.Fold(p.DisplayName) == DevOpsDeskMapping.Fold(name));
                if (match is not null && Guid.TryParse(match.Id, out var id))
                    result.Add(new(name, id.ToString()));
            }
            catch (Exception e) when (!ct.IsCancellationRequested && e is DevOpsActionException or HttpRequestException or JsonException)
            {
                logger.LogInformation("Mesa de gestão: menção de {Name} sem identidade no DevOps (fica texto): {Error}", name, e.Message);
            }
        }
        return result;
    }

    public async Task<DevOpsCommentsResponse> GetCommentsAsync(int id, int top = 200, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw DevOpsDeskException.Invalid("Informe o id do card.");
        await EnsureConfigAsync(cancellationToken);
        using var doc = await GetAsync($"{ProjectApi}/wit/workItems/{id}/comments?$top={Math.Clamp(top, 1, 200)}&order=desc&$expand=renderedText&api-version={CommentsApi}",
            $"O DevOps não devolveu a discussion do card {id}", cancellationToken);
        return DevOpsDeskMapping.ParseComments(doc.RootElement);
    }

    public async Task<IReadOnlyList<DevOpsAttachmentInfo>> GetAttachmentsAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw DevOpsDeskException.Invalid("Informe o id do card.");
        await EnsureConfigAsync(cancellationToken);
        using var doc = await GetAsync($"{ProjectApi}/wit/workitems/{id}?$expand=all&api-version={Api}", $"O DevOps não devolveu o card {id}", cancellationToken);
        DevOpsCommentsResponse comments;
        try
        {
            comments = await GetCommentsAsync(id, 200, cancellationToken);
        }
        catch (DevOpsActionException)
        {
            // Sem a discussion: seguem os anexos do card e os dos campos.
            comments = new DevOpsCommentsResponse(0, []);
        }
        return DevOpsDeskMapping.ParseAttachments(doc.RootElement, DevOpsDeskMapping.EvidenceComments(comments.Comments));
    }

    public async Task<DevOpsAttachmentContent> DownloadAttachmentAsync(Guid attachmentId, string? name, long maxBytes, CancellationToken cancellationToken = default)
    {
        if (attachmentId == Guid.Empty) throw DevOpsDeskException.Invalid("Informe o anexo.");
        await EnsureConfigAsync(cancellationToken);
        var fileName = string.IsNullOrWhiteSpace(name) ? $"anexo-{attachmentId.ToString()[..8]}" : AzureCardEvidenceParser.SafeName(name.Trim());
        var url = $"{OrganizationUrl}/_apis/wit/attachments/{attachmentId}?fileName={Uri.EscapeDataString(fileName)}&download=true&api-version={Api}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
            throw new DevOpsDeskException(403, "devops_forbidden",
                "O DevOps não aceitou o token (PAT) da sua integração — confira em \"Minhas integrações\" (AzureDevOps Configurations).");
        if (!response.IsSuccessStatusCode)
            throw DevOpsDeskException.FromDevOps((int)response.StatusCode,
                $"O DevOps não devolveu o anexo {fileName}: {ExtractDevOpsError(await response.Content.ReadAsStringAsync(cancellationToken))}");
        var media = response.Content.Headers.ContentType?.MediaType;
        var contentType = string.IsNullOrWhiteSpace(media) || media.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            ? DevOpsDeskMapping.ContentTypeFromName(fileName)
            : media;
        var declared = response.Content.Headers.ContentLength;
        if (declared > maxBytes) return new DevOpsAttachmentContent([], contentType, fileName, true, declared);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes) return new DevOpsAttachmentContent([], contentType, fileName, true, null);
        }
        return new DevOpsAttachmentContent(buffer.ToArray(), contentType, fileName, false, buffer.Length);
    }
}
