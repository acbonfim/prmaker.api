using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.azure.application.Contract;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;
using solvace.prform.application.UserIntegrations;

namespace solvace.prform.Execution;

/// <summary>
/// 0099 (Mesa de gestão): o Azure DevOps pelo MCP — metadados/picklists, consultas, ler/criar/editar/mover cards, com a
/// integração de quem age (no especialista, a de quem pediu: as de escrita estão em <see cref="CaseSpecialistMiddleware.ActingTools"/>).
/// Mesmo efeito da tela (<see cref="DevOpsDeskRunner"/>: Timeline + <c>devopsCardsUpdated</c>). Guardião do caso: chave de
/// épico (<c>E123</c>) ⇒ qualquer card; chave de card ⇒ só o próprio card (criar só como filho dele).
/// </summary>
[McpServerToolType]
public partial class DevOpsMcpTools(IDevOpsDeskService devops, DevOpsDeskRunner runner, SpecialistActions specialist, IHttpContextAccessor http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Name = "prmake_devops_meta", ReadOnly = true)]
    [Description("Metadados do Azure DevOps do projeto (com a integracao de quem age). what: types (tipos com cor) | fields (campos do 'type' com " +
                 "tipo, obrigatorio, somente leitura e allowedValues do picklist) | tags (do projeto, 'q' filtra por contem) | areas | iterations " +
                 "(caminhos no formato do campo) | people (membros dos times, 'q' filtra) | teams | boards ('team'; sem time = o padrao) | board " +
                 "(colunas com stateMappings tipo->estado, limite WIP, coluna dividida, lanes, campos coluna/lane/done, campo de ordem; 'team'/'board'). " +
                 "Use antes de criar/editar/mover: valores de picklist, tags e colunas existentes.")]
    public Task<string> Meta(
        [Description("types | fields | tags | areas | iterations | people | teams | boards | board")] string what,
        [Description("fields: tipo do work item (Bug, User Story...)")] string? type = null,
        [Description("tags/people: filtro (contem)")] string? q = null,
        [Description("boards/board: time (padrao: o time padrao do projeto)")] string? team = null,
        [Description("board: nome ou id do board (padrao: o do backlog de requisitos)")] string? board = null,
        CancellationToken ct = default) => Safe(async () => (what ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "types" => Serialize(await devops.GetTypesAsync(ct)),
        "fields" => Serialize(await devops.GetFieldsAsync(type ?? throw new McpException("fields: informe 'type'."), ct)),
        "tags" => Serialize(await devops.GetTagsAsync(q, ct)),
        "areas" => Serialize((await devops.GetAreasAsync(ct)).Select(n => n.Path)),
        "iterations" => Serialize((await devops.GetIterationsAsync(ct)).Select(n => n.Path)),
        "people" => Serialize((await devops.GetPeopleAsync(q, ct)).Take(100).Select(p => new { p.DisplayName, p.UniqueName })),
        "teams" => Serialize((await devops.GetTeamsAsync(ct)).Select(t => t.Name)),
        "boards" => Serialize((await devops.GetBoardsAsync(team, ct)).Select(b => b.Name)),
        "board" => Serialize(await devops.GetBoardAsync(team, board, null, ct)),
        _ => throw new McpException($"what desconhecido: {what} (types|fields|tags|areas|iterations|people|teams|boards|board).")
    });

    [McpServerTool(Name = "prmake_devops_query", ReadOnly = true)]
    [Description("Consulta cards do DevOps: 'wiql' pronta OU filtros (types, states, areaPath/iterationPath (UNDER), tags (qualquer uma), assignedTo " +
                 "(login ou @me), text (titulo contem ou o id), createdFrom/createdTo/changedFrom (yyyy-MM-dd)) — valores do mesmo filtro em OU, " +
                 "filtros diferentes em E. 'fields' = referenceNames (padrao: id, tipo, titulo, estado, responsavel, tags, area, iteracao, datas, " +
                 "prioridade, coluna). Devolve {count, items:[{id, fields}]} (responsavel como nome). top padrao 200 (max 2000).")]
    public Task<string> Query(
        [Description("WIQL completa (SELECT [System.Id] FROM WorkItems WHERE ...)")] string? wiql = null,
        string[]? types = null, string[]? states = null, string? areaPath = null, string? iterationPath = null, string[]? tags = null,
        string[]? assignedTo = null, string? text = null, string? createdFrom = null, string? createdTo = null, string? changedFrom = null,
        [Description("referenceNames dos campos a devolver")] string[]? fields = null,
        int? top = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var result = await devops.QueryAsync(new DevOpsQueryRequest
        {
            Wiql = wiql,
            Filter = new DevOpsQueryFilter
            {
                Types = types?.ToList(), States = states?.ToList(), AreaPath = areaPath, IterationPath = iterationPath, Tags = tags?.ToList(),
                AssignedTo = assignedTo?.ToList(), Text = text, CreatedFrom = Date(createdFrom, "createdFrom"), CreatedTo = Date(createdTo, "createdTo"),
                ChangedFrom = Date(changedFrom, "changedFrom")
            },
            Fields = fields?.ToList(),
            Top = top ?? 200
        }, ct);
        return Serialize(new { result.Count, items = result.Items.Select(i => new { i.Id, fields = Compact(i.Fields) }) });
    });

    [McpServerTool(Name = "prmake_devops_card_get", ReadOnly = true)]
    [Description("Card do DevOps com todos os campos (responsavel como nome), tags, relacoes (pai/filhos/links com id) e o link.")]
    public Task<string> CardGet(int id, CancellationToken ct = default) => Safe(async () =>
    {
        var card = await devops.GetCardAsync(id, ct);
        return Serialize(new
        {
            card.Id, card.Rev, card.Type, card.Title, card.State, card.Tags, card.Url,
            fields = Compact(card.Fields),
            relations = card.Relations.Select(r => new { r.Rel, r.Id, r.Name, url = r.Id is null ? r.Url : null })
        });
    });

    [McpServerTool(Name = "prmake_devops_card_create", Destructive = true)]
    [Description("Cria um card no DevOps (confira duplicado com prmake_devops_query antes). type = tipo do projeto (prmake_devops_meta types); " +
                 "fields = {referenceName: valor} (picklist: um allowedValue de prmake_devops_meta fields; area/iteracao no formato do campo; " +
                 "System.Description em HTML); tags = lista (existentes ou novas); parentId = pai (relacao Hierarchy); epic = numero do epico da Mesa " +
                 "(entra nele como 'created'; com o token de um epico, o proprio epico por padrao); column/lane iniciais opcionais (team/board: padrao " +
                 "o time padrao e o board do tipo). Registra na Timeline do card. Especialista: so a pedido da pessoa (integracao de quem pediu); " +
                 "no token de um card, so filho dele.")]
    public Task<string> CardCreate(string type, string title,
        [Description("{referenceName: valor} (objeto JSON ou o JSON em texto)")] JsonElement? fields = null,
        string[]? tags = null, int? parentId = null, long? epic = null, string? column = null, string? lane = null,
        string? team = null, string? board = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var own = OwnCard();
        Check(CaseScopeError(own, null, true, parentId));
        epic ??= EpicNumber(own);
        var target = parentId is > 0 ? parentId.Value.ToString() : own ?? "?";
        return await specialist.RunAsync(target, $"Criar {type} «{Short(title)}» no DevOps", null, async () =>
        {
            var result = await runner.CreateAsync(new DevOpsCreateCardRequest
            {
                Type = type, Title = title, Fields = FieldMap(fields), Tags = tags?.ToList(), ParentId = parentId, Epic = epic,
                Column = column, Lane = lane, Team = team, Board = board
            }, User, ct, specialist.Via);
            return (Serialize(new { result.Card.Id, result.Card.Type, result.Card.Title, result.Card.State, result.Card.Url, summary = result.Summary }),
                result.Summary);
        }, ct);
    });

    [McpServerTool(Name = "prmake_devops_card_update", Destructive = true)]
    [Description("Edita um card do DevOps: fields = {referenceName: valor} (null limpa o campo; picklist com allowedValue), addTags/removeTags " +
                 "(somadas/tiradas das atuais; tag nova e criada), comment = comentario na discussion (markdown). Registra na Timeline. " +
                 "Especialista: so a pedido da pessoa; no token de um card, so o proprio card.")]
    public Task<string> CardUpdate(int id,
        [Description("{referenceName: valor|null} (objeto JSON ou o JSON em texto)")] JsonElement? fields = null,
        string[]? addTags = null, string[]? removeTags = null, string? comment = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        Check(CaseScopeError(OwnCard(), id, false, null));
        return await specialist.RunAsync(id.ToString(), $"Editar o card #{id} no DevOps", null, async () =>
        {
            var result = await runner.UpdateAsync(id, new DevOpsUpdateCardRequest
            {
                Fields = FieldMap(fields), AddTags = addTags?.ToList(), RemoveTags = removeTags?.ToList(), Comment = comment
            }, User, ct, specialist.Via);
            return (Serialize(new { result.Card.Id, result.Card.Rev, result.Card.State, result.Card.Tags, summary = result.Summary }), result.Summary);
        }, ct);
    });

    [McpServerTool(Name = "prmake_devops_card_move", Destructive = true)]
    [Description("Move um card no board do DevOps como o arrastar da tela: column (nome ou id; o estado vem do mapeamento da coluna para o tipo — " +
                 "coluna sem mapeamento recusa), lane (nome; 'default' = a padrao), done (coluna dividida Doing/Done), previousId/nextId (ordem: " +
                 "depois/antes desse card). team/board: padrao o time padrao e o board que tem o tipo (prmake_devops_meta board mostra as colunas). " +
                 "Registra na Timeline. Especialista: so a pedido da pessoa; no token de um card, so o proprio card.")]
    public Task<string> CardMove(int id, string column, string? lane = null, bool? done = null, string? team = null, string? board = null,
        int? previousId = null, int? nextId = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        Check(CaseScopeError(OwnCard(), id, false, null));
        return await specialist.RunAsync(id.ToString(), $"Mover o card #{id} para {column}", null, async () =>
        {
            var result = await runner.MoveAsync(id, new DevOpsMoveCardRequest
            {
                Column = column, Lane = lane, Done = done, Team = team, Board = board, PreviousId = previousId, NextId = nextId
            }, User, ct, specialist.Via);
            return (Serialize(new { result.Card.Id, result.Card.Rev, result.Card.State, summary = result.Summary }), result.Summary);
        }, ct);
    });

    // ---------------------------------------------------------------- guardião do caso

    /// <summary>Chave do caso é de épico (<c>E123</c>)?</summary>
    public static bool IsEpicKey(string? key) => solvace.executionplans.domain.Entities.EpicKey.IsEpic(key);

    private static long? EpicNumber(string? key) =>
        solvace.executionplans.domain.Entities.EpicKey.TryParse(key, out var n) ? n : null;

    /// <summary>
    /// Regra do guardião para as gravações (null = pode): fora do token do caso, tudo; chave de épico, qualquer card; chave de
    /// card, só o próprio card (<paramref name="target"/>) e criar só como filho dele (<paramref name="parentId"/>).
    /// </summary>
    public static string? CaseScopeError(string? ownCard, int? target, bool create, int? parentId)
    {
        if (string.IsNullOrWhiteSpace(ownCard) || IsEpicKey(ownCard)) return null;
        var own = ownCard.Trim().TrimStart('#');
        if (create)
            return parentId?.ToString(CultureInfo.InvariantCulture) == own
                ? null
                : $"No caso do card {own} o especialista so cria card filho dele (parentId = {own}). Para outros cards, use a Mesa de gestao (epico).";
        return target?.ToString(CultureInfo.InvariantCulture) == own
            ? null
            : $"No caso do card {own} o especialista so altera o proprio card no DevOps. Para outros cards, use a Mesa de gestao (epico).";
    }

    private static void Check(string? error)
    {
        if (error is not null) throw new McpException(error);
    }

    private string? OwnCard() => CaseClaims.CardOf(User)?.Trim().TrimStart('#');

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    // ---------------------------------------------------------------- utilidades

    /// <summary><c>fields</c> do MCP (objeto JSON ou o JSON em texto) → referenceName → valor.</summary>
    public static Dictionary<string, JsonElement>? FieldMap(JsonElement? fields)
    {
        if (fields is not { } f || f.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (f.ValueKind == JsonValueKind.String)
        {
            var text = f.GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                return FieldMap(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
                throw new McpException("fields: informe um objeto JSON {referenceName: valor}.");
            }
        }
        if (f.ValueKind != JsonValueKind.Object)
            throw new McpException("fields: informe um objeto JSON {referenceName: valor}.");
        return f.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    /// <summary>Campos para o modelo: identidade vira "Nome &lt;login&gt;", o resto como veio.</summary>
    private static Dictionary<string, object?> Compact(Dictionary<string, JsonElement> fields) =>
        fields.ToDictionary(p => p.Key, p => p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("displayName", out var dn)
            ? (object?)(p.Value.TryGetProperty("uniqueName", out var un) && un.GetString() is { Length: > 0 } login ? $"{dn.GetString()} <{login}>" : dn.GetString())
            : p.Value);

    private static DateTimeOffset? Date(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : throw new McpException($"{name}: data invalida (use yyyy-MM-dd).");
    }

    private static string Short(string value) => value.Length <= 60 ? value.Trim() : value[..60].TrimEnd() + "…";

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    private static async Task<string> Safe(Func<Task<string>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception e) when (e is DevOpsActionException or PersonalIntegrationRequiredException or InvalidOperationException or HttpRequestException)
        {
            throw new McpException(e.Message, e);
        }
    }
}
