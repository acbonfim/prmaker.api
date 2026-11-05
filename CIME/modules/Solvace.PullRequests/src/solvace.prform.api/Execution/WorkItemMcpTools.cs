using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// 0085: regras do PRMake para o card e itens de trabalho pelo MCP. O Especialista do caso (token do caso) lê as regras,
/// lista/cria/edita itens pending do próprio card (<see cref="CaseSpecialistMiddleware.AllowedTools"/>); a execução das
/// sub-tarefas (<c>prmake_work_task</c>) é do executor/skill. Duplicata (mesmo fingerprint) volta como mensagem clara
/// com o item existente e como forçar.
/// </summary>
[McpServerToolType]
public class WorkItemMcpTools(
    IWorkItemApplication items,
    IExecutionPlanApplication plans,
    SkillsRulesService rules,
    WorkItemToolsService tools,
    IHttpContextAccessor http,
    solvace.timeline.application.Contracts.IUserRepository users)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Name = "prmake_rules", ReadOnly = true)]
    [Description("Regras do PRMake calculadas para o card (markdown): tipo (Bug/US) e o que muda, fluxo pela area, branch principal, " +
                 "NOME DE BRANCH POR DESTINO (ex.: release-version -> hotfix/<card>-rv), PRs do fluxo por tipo de repositorio, padroes de " +
                 "commit/titulo, promocao e regras fixas (nunca mesclar, toda gravacao pelo PRMake, itens sem duplicar). Use antes de propor " +
                 "ou criar branch/PR/item — nunca invente nome de branch.")]
    public Task<string> Rules(string card, CancellationToken ct = default) => Safe(async () =>
        SkillsRulesService.Markdown(await rules.GetAsync(RequireCard(card), ct)));

    [McpServerTool(Name = "prmake_work_items", ReadOnly = true)]
    [Description("Itens de trabalho do card (resumo): numero, tipo, titulo, status, origem, fingerprint, plano ligado, se ainda e editavel e " +
                 "as sub-tarefas (numero, ordem, kind, titulo, status, executor). Confira aqui antes de criar item — nao duplique.")]
    public Task<string> List(string card, CancellationToken ct = default) => Safe(async () =>
    {
        var list = await items.ListAsync(RequireCard(card), ct);
        return Serialize(new
        {
            card = RequireCard(card),
            items = list.Select(i => new
            {
                i.Number, i.Type, i.Title, i.Status, i.Origin, i.TemplateKey, i.PlanId, i.PlanPhase, i.Editable, i.Fingerprint,
                progress = $"{i.Progress.Done}/{i.Progress.Total}",
                tasks = i.Tasks.OrderBy(t => t.Order).Select(t => new { t.Number, t.Order, t.Kind, t.Title, t.Status, t.Executor })
            })
        });
    });

    [McpServerTool(Name = "prmake_work_item", ReadOnly = true)]
    [Description("Detalhe de um item de trabalho do card: sub-tarefas com params, resultado, saidas (output), como desfazer (undo), " +
                 "links, pedido da fila e o historico recente.")]
    public Task<string> Get(string card, [Description("Numero do item (#n)")] long number, CancellationToken ct = default) => Safe(async () =>
    {
        var item = await items.GetAsync(number, true, ct);
        EnsureSameCard(item, card);
        return Serialize(item);
    });

    [McpServerTool(Name = "prmake_work_item_create")]
    [Description("Cria um item de trabalho no card com sub-tarefas ordenadas. Antes, liste (prmake_work_items) e use as regras " +
                 "(prmake_rules) para nomes de branch/destinos. Mesmo objetivo de item existente (fingerprint) e recusado: use o existente " +
                 "ou mande allowDuplicate=true com duplicateReason. Se uma ferramenta (prmake_tools) serve, prefira prmake_tool_run. tasks = lista " +
                 "[{ title, kind (branch|cherry-pick|code|pr|pr-review|devops-state|devops-action|pr-text|sql-query|sql-script|validation|comment|" +
                 "manual|ticket|notify), description?, executor? (claude|user), params? {repository, branch, source, target, " +
                 "from, commits, state, ...} }] (lista ou o JSON dela em texto). Origem: especialista (token do caso) ou a fase do plano aberto.")]
    public Task<string> Create(string card, string title,
        [Description("Sub-tarefas em ordem: [{title, kind, description?, executor?, params?}] (lista ou JSON em texto)")] JsonElement tasks,
        string? description = null,
        [Description("Tipo do item (WorkItemTypes: task, pull-request, review, adjustment, closing, data, ticket, text, fix, implementation, analysis; padrao: o do modelo ou task). Nunca bug/user-story (sao do card no DevOps).")] string? type = null,
        [Description("Modelo de origem (catalogo GET WorkItems/catalog), se veio de um")] string? templateKey = null,
        [Description("analysis | correction — fase do plano que criou (padrao: a do plano aberto)")] string? planPhase = null,
        [Description("Cria mesmo havendo item igual — exige duplicateReason")] bool allowDuplicate = false,
        string? duplicateReason = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        EnsureSpecialistCard(c);
        var (origin, phase) = await OriginAsync(c, planPhase, ct);
        var request = new CreateWorkItemRequest
        {
            Title = title, Description = description, Type = type, TemplateKey = templateKey, Tasks = ParseTasks(tasks),
            AllowDuplicate = allowDuplicate, DuplicateReason = duplicateReason, PlanPhase = phase
        };
        try
        {
            var created = await items.CreateAsync(c, request, origin, await ActorAsync(ct), ct);
            return Serialize(new
            {
                created.Number, created.Title, created.Status, created.Origin, created.Fingerprint,
                tasks = created.Tasks.OrderBy(t => t.Order).Select(t => new { t.Number, t.Order, t.Kind, t.Title })
            });
        }
        catch (WorkItemDuplicateException d)
        {
            throw new McpException(
                $"Ja existe o item #{d.ExistingNumber} \"{d.ExistingTitle}\" ({d.ExistingStatus}) com o mesmo objetivo neste card — nao criei outro. " +
                $"Use o existente (prmake_work_item card {c} number {d.ExistingNumber}); se for de fato outro trabalho, chame de novo com " +
                "allowDuplicate=true e duplicateReason explicando a diferenca.", d);
        }
    });

    [McpServerTool(Name = "prmake_tools", ReadOnly = true)]
    [Description("Ferramentas (modelos de item de trabalho) para o card: key, titulo, descricao, tipo do item, se se aplica ao card " +
                 "(applicable + reason), as perguntas (ask: key, label, input, options, default, required) e as sub-tarefas. Use antes de " +
                 "criar item: se o pedido bate com uma ferramenta, rode-a com prmake_tool_run (o PRMake monta branch/destino/sufixo certos).")]
    public Task<string> Tools(string card, CancellationToken ct = default) => Safe(async () =>
    {
        var catalog = await tools.CatalogAsync(RequireCard(card), ct);
        var list = (catalog["templates"] as System.Text.Json.Nodes.JsonArray ?? []).OfType<System.Text.Json.Nodes.JsonObject>()
            .Select(t => new System.Text.Json.Nodes.JsonObject
            {
                ["key"] = t["key"]?.DeepClone(),
                ["title"] = t["title"]?.DeepClone(),
                ["description"] = t["description"]?.DeepClone(),
                ["type"] = t["type"]?.DeepClone(),
                ["group"] = t["group"]?.DeepClone(),
                ["applicable"] = t["applicable"]?.DeepClone(),
                ["reason"] = t["reason"]?.DeepClone(),
                ["ask"] = t["ask"]?.DeepClone(),
                ["tasks"] = new System.Text.Json.Nodes.JsonArray((t["tasks"] as System.Text.Json.Nodes.JsonArray ?? []).OfType<System.Text.Json.Nodes.JsonObject>()
                    .Select(x => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject { ["kind"] = x["kind"]?.DeepClone(), ["title"] = x["title"]?.DeepClone() })
                    .ToArray())
            });
        var result = new System.Text.Json.Nodes.JsonObject
        {
            ["card"] = catalog["card"]?.DeepClone(),
            ["cardType"] = catalog["type"]?.DeepClone(),
            ["mainBranch"] = catalog["mainBranch"]?.DeepClone(),
            ["repository"] = catalog["repository"]?.DeepClone(),
            ["repositories"] = catalog["repositories"]?.DeepClone(),
            ["repoKind"] = catalog["repoKind"]?.DeepClone(),
            ["flow"] = catalog["flow"]?.DeepClone(),
            ["tools"] = new System.Text.Json.Nodes.JsonArray(list.Select(x => (System.Text.Json.Nodes.JsonNode?)x).ToArray())
        };
        return result.ToJsonString(Json);
    });

    [McpServerTool(Name = "prmake_tool_run")]
    [Description("Cria o item de trabalho a partir de uma ferramenta (prmake_tools) com as respostas das perguntas (ask). O PRMake renderiza " +
                 "branch, sufixo do destino, repositorio e destinos; confere obrigatorias, destino invalido para o tipo de repositorio e se a " +
                 "ferramenta se aplica ao card. Antes, liste os itens (prmake_work_items): mesmo objetivo de item existente e recusado — use o " +
                 "existente ou mande allowDuplicate=true com duplicateReason. Quem executa o item e a pessoa (Bancada).")]
    public Task<string> ToolRun(string card,
        [Description("key da ferramenta (prmake_tools)")] string key,
        [Description("Respostas {key: valor} das perguntas (objeto ou o JSON em texto); vazio = os padroes")] JsonElement? answers = null,
        [Description("Titulo do item (opcional; padrao: o da ferramenta)")] string? title = null,
        [Description("Cria mesmo havendo item igual — exige duplicateReason")] bool allowDuplicate = false,
        string? duplicateReason = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        EnsureSpecialistCard(c);
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (AsObject(answers) is { } obj)
            foreach (var p in obj.EnumerateObject())
                map[p.Name] = p.Value.Clone();
        CreateWorkItemRequest request;
        try
        {
            request = await tools.BuildCreateRequestAsync(c, new CreateFromTemplateRequest
            {
                TemplateKey = key, Answers = map, Title = title, AllowDuplicate = allowDuplicate, DuplicateReason = duplicateReason
            }, ct);
        }
        catch (WorkItemTemplateException e)
        {
            throw new McpException(e.Missing.Count > 0 ? $"{e.Message} Faltando: {string.Join(", ", e.Missing)}." : e.Message, e);
        }
        var origin = CaseClaims.SpecialistOf(User) is not null ? WorkItemOrigin.Specialist : WorkItemOrigin.Template;
        try
        {
            var created = await items.CreateAsync(c, request, origin, await ActorAsync(ct), ct);
            return Serialize(new
            {
                created.Number, created.Type, created.Title, created.Status, created.Origin, created.TemplateKey, created.Fingerprint,
                tasks = created.Tasks.OrderBy(t => t.Order).Select(t => new { t.Number, t.Order, t.Kind, t.Title, t.Params })
            });
        }
        catch (WorkItemDuplicateException d)
        {
            throw new McpException(
                $"Ja existe o item #{d.ExistingNumber} \"{d.ExistingTitle}\" ({d.ExistingStatus}) com o mesmo objetivo neste card — nao criei outro. " +
                $"Use o existente (prmake_work_item card {c} number {d.ExistingNumber}); se for de fato outro trabalho, chame de novo com " +
                "allowDuplicate=true e duplicateReason explicando a diferenca.", d);
        }
    });

    [McpServerTool(Name = "prmake_work_item_update")]
    [Description("Edita um item de trabalho AINDA pendente (todas as sub-tarefas pending): titulo, descricao e/ou sub-tarefas " +
                 "(tasks substitui a lista; mande 'number' de uma sub-tarefa existente para mante-la). Item iniciado/concluido nao edita: " +
                 "crie um novo.")]
    public Task<string> Update(string card, [Description("Numero do item (#n)")] long number, string? title = null, string? description = null,
        [Description("Nova lista de sub-tarefas (opcional): [{number?, title, kind, description?, executor?, params?}]")] JsonElement? tasks = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        EnsureSpecialistCard(c);
        var current = await items.GetAsync(number, false, ct);
        EnsureSameCard(current, c);
        var updated = await items.UpdateAsync(number, new UpdateWorkItemRequest
        {
            Title = title ?? current.Title,
            Description = description ?? current.Description,
            Tasks = tasks is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } t ? ParseTasks(t) : null
        }, await ActorAsync(ct), ct);
        return Serialize(new
        {
            updated.Number, updated.Title, updated.Status, updated.Fingerprint,
            tasks = updated.Tasks.OrderBy(x => x.Order).Select(x => new { x.Number, x.Order, x.Kind, x.Title, x.Status })
        });
    });

    [McpServerTool(Name = "prmake_work_task")]
    [Description("Registra o andamento de uma sub-tarefa de item de trabalho: action start | complete | fail | wait | skip | revert. " +
                 "result = markdown curto (em fail/wait: o motivo / o que a pessoa precisa fazer); output = fatos (pr, number, repository, " +
                 "branch, target, commit, commits, created, from); undo = {action, params} de como desfazer (ao concluir); links = " +
                 "[{kind: pr|branch|commit|doc, title, url}] para o item.")]
    public Task<string> TaskAction(string card, [Description("Numero da sub-tarefa (#n)")] long taskNumber,
        [Description("start | complete | fail | wait | skip | revert")] string action,
        string? result = null,
        [Description("Objeto JSON (ou o JSON em texto) com as saidas")] JsonElement? output = null,
        [Description("{action, params} (ou o JSON em texto) — como desfazer")] JsonElement? undo = null,
        [Description("[{kind, title, url}] (ou o JSON em texto) — links para o item")] JsonElement? links = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        if (!(await items.ListAsync(c, ct)).Any(i => i.Tasks.Any(t => t.Number == taskNumber)))
            throw new McpException($"A sub-tarefa #{taskNumber} nao e de um item do card {c}.");
        var request = new WorkTaskActionRequest
        {
            Result = result,
            Output = AsObject(output),
            Undo = AsObject(undo) is { } u ? ParseUndo(u) : null,
            Links = AsJson(links) is { ValueKind: JsonValueKind.Array } l ? l.Deserialize<List<WorkItemLink>>(Json) : null
        };
        var item = await items.TaskActionAsync(taskNumber, (action ?? string.Empty).Trim().ToLowerInvariant(), request, await ActorAsync(ct), ct);
        var task = item.Tasks.FirstOrDefault(t => t.Number == taskNumber);
        return Serialize(new
        {
            item = new { item.Number, item.Status, progress = $"{item.Progress.Done}/{item.Progress.Total}" },
            task = task is null ? null : new { task.Number, task.Status },
            next = item.Tasks.OrderBy(t => t.Order).FirstOrDefault(t => t.Status is WorkTaskStatus.Pending or WorkTaskStatus.Waiting) is { } n
                ? new { n.Number, n.Title, n.Kind, n.Status }
                : null
        });
    });

    // ── Internos ─────────────────────────────────────────────────────────────────────────────────

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    /// <summary>Origem do item: especialista (token do caso); senão a fase informada ou a do plano aberto; senão "user".</summary>
    private async Task<(string Origin, string? Phase)> OriginAsync(string card, string? planPhase, CancellationToken ct)
    {
        if (CaseClaims.SpecialistOf(User) is not null)
            return (WorkItemOrigin.Specialist, null);
        var phase = planPhase?.Trim().ToLowerInvariant();
        if (phase is not (ExecutionPhase.Analysis or ExecutionPhase.Correction))
        {
            var open = (await plans.GetByCardAsync(card, ct))
                .Where(s => s.Status is not (ExecutionStatus.Completed or ExecutionStatus.Cancelled))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();
            phase = open?.Phase;
        }
        return phase switch
        {
            ExecutionPhase.Analysis => (WorkItemOrigin.Analysis, phase),
            ExecutionPhase.Correction => (WorkItemOrigin.Correction, phase),
            _ => (WorkItemOrigin.User, null)
        };
    }

    private async Task<ExecutionActor> ActorAsync(CancellationToken ct)
    {
        if (CaseClaims.SpecialistOf(User) is { } specialist)
            return specialist.AsExecutionActor();
        Guid? userId = Guid.TryParse(User.FindFirst("ExternalId")?.Value, out var id) ? id : null;
        string? name = userId.HasValue ? await users.GetFullNameAsync(userId.Value, ct) : null;
        name ??= User.FindFirst(ClaimTypes.Name)?.Value;
        return new ExecutionActor(userId, string.IsNullOrWhiteSpace(name) ? "Claude" : name.Trim(), true);
    }

    /// <summary>O especialista só mexe nos itens do card dele.</summary>
    private void EnsureSpecialistCard(string card)
    {
        if (CaseClaims.SpecialistOf(User) is not null && CaseClaims.CardOf(User) is { } own && !string.Equals(own.TrimStart('#'), card, StringComparison.Ordinal))
            throw new McpException("O Especialista do caso so cria/edita itens do card dele.");
    }

    private static void EnsureSameCard(WorkItemResponse item, string card)
    {
        if (!string.Equals(item.CardNumber, RequireCard(card), StringComparison.Ordinal))
            throw new McpException($"O item #{item.Number} e do card {item.CardNumber}, nao do {RequireCard(card)}.");
    }

    private static string RequireCard(string? card)
    {
        var c = SkillsRulesService.NormalizeCard(card);
        return c.Length > 0 ? c : throw new McpException("Informe o card.");
    }

    /// <summary>Elemento JSON ou texto com JSON → o elemento (null/indefinido → null).</summary>
    private static JsonElement? AsJson(JsonElement? value)
    {
        if (value is not { } v || v.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String) return v;
        var text = v.GetString()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException e)
        {
            throw new McpException($"JSON invalido: {e.Message}");
        }
    }

    private static JsonElement? AsObject(JsonElement? value) =>
        AsJson(value) is { } v ? v.ValueKind == JsonValueKind.Object ? v : throw new McpException("Esperava um objeto JSON ({...}).") : null;

    private static WorkTaskUndo ParseUndo(JsonElement e) => new()
    {
        Action = Str(e, "action") ?? string.Empty,
        Params = StringMap(e, "params")
    };

    /// <summary>Sub-tarefas tolerantes: lista ou JSON em texto; valores dos params viram texto.</summary>
    private static List<WorkTaskInput> ParseTasks(JsonElement tasks)
    {
        var array = AsJson(tasks) ?? throw new McpException("Informe as sub-tarefas (tasks).");
        if (array.ValueKind != JsonValueKind.Array)
            throw new McpException("tasks deve ser uma lista [{title, kind, ...}].");
        var list = new List<WorkTaskInput>();
        foreach (var t in array.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object)
                throw new McpException("Cada sub-tarefa deve ser um objeto {title, kind, ...}.");
            list.Add(new WorkTaskInput
            {
                Number = t.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out var num) ? num : null,
                Title = Str(t, "title"),
                Description = Str(t, "description"),
                Kind = Str(t, "kind"),
                Executor = Str(t, "executor"),
                StepKey = Str(t, "stepKey"),
                Params = StringMap(t, "params")
            });
        }
        return list;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => v.GetRawText()
        } : null;

    private static Dictionary<string, string> StringMap(JsonElement e, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!e.TryGetProperty(name, out var p)) return map;
        if (p.ValueKind == JsonValueKind.String && AsJson(p) is { ValueKind: JsonValueKind.Object } parsed) p = parsed;
        if (p.ValueKind != JsonValueKind.Object) return map;
        foreach (var prop in p.EnumerateObject())
            map[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                JsonValueKind.Array => string.Join(",", prop.Value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText())),
                _ => prop.Value.GetRawText()
            };
        return map;
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    /// <summary>Erro de regra (item não editável, duplicata, não encontrado...) vai com a mensagem para o Claude.</summary>
    private static async Task<T> Safe<T>(Func<Task<T>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception e) when (e is DomainException or ExecutionPlanNotFoundException or ExecutionForbiddenException
                                       or ExecutionPlanConcurrencyException or ArgumentException
                                       or solvace.prform.application.UserIntegrations.PersonalIntegrationRequiredException
                                       or InvalidOperationException)
        {
            throw new McpException(e.Message, e);
        }
    }
}
