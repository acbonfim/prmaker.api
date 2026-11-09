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
/// lista/cria/edita itens pending do próprio card (<see cref="CaseSpecialistMiddleware.AllowedTools"/>) e (r5) cancela,
/// executa e registra o andamento de itens a pedido de uma pessoa — nunca de sub-tarefa espelho do plano. Duplicata (mesmo
/// fingerprint) volta como mensagem clara com o item existente e como forçar.
/// </summary>
[McpServerToolType]
public class WorkItemMcpTools(
    IWorkItemApplication items,
    IExecutionPlanApplication plans,
    SkillsRulesService rules,
    WorkItemToolsService tools,
    IHttpContextAccessor http,
    solvace.timeline.application.Contracts.IUserRepository users,
    SpecialistActions specialist,
    CodeCitationGate citations,
    IExecutionQueueApplication queue,
    IWorkItemDeliverableApplication deliverables)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Name = "prmake_rules", ReadOnly = true)]
    [Description("Regras do PRMake calculadas para o card (markdown): tipo (Bug/US) e o que muda, fluxo pela area, branch principal, " +
                 "NOME DE BRANCH POR DESTINO (ex.: release-version -> hotfix/<card>-rv), PRs do fluxo por tipo de repositorio, padroes de " +
                 "commit/titulo, branches/destinos POR REPOSITORIO DO CARD (revamp-backend nao tem QA), promocao e regras fixas (nunca mesclar, toda gravacao pelo PRMake, itens sem duplicar). Use antes de propor " +
                 "ou criar branch/PR/item — nunca invente nome de branch.")]
    public Task<string> Rules(string card, CancellationToken ct = default) => Safe(async () =>
    {
        var r = await rules.GetAsync(RequireCard(card), ct);
        return SkillsRulesService.Markdown(r, await tools.RepositoryPlansAsync(r, ct));
    });

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
    [Description("Detalhe de um item de trabalho: sub-tarefas com params, resultado, saidas (output), como desfazer (undo), " +
                 "links, pedido da fila e o historico recente. Aceita o #n citado na conversa: numero de sub-tarefa devolve o item dono " +
                 "com matchedTaskNumber; item de outro card (citado como referencia) vem com otherCard=true — so leitura, nao altere.")]
    public Task<string> Get(string card, [Description("Numero do item ou da sub-tarefa (#n)")] long number, CancellationToken ct = default) => Safe(async () =>
    {
        var item = await items.ResolveAsync(number, true, ct);
        var otherCard = !string.Equals(item.CardNumber, RequireCard(card), StringComparison.Ordinal);
        return otherCard ? Serialize(new { otherCard = true, note = $"Item do card {item.CardNumber} — referencia, so leitura.", item }) : Serialize(item);
    });

    [McpServerTool(Name = "prmake_work_item_create")]
    [Description("Cria um item de trabalho no card com sub-tarefas ordenadas. Antes, liste (prmake_work_items) e use as regras " +
                 "(prmake_rules) para nomes de branch/destinos. Mesmo objetivo de item existente (fingerprint) e recusado: use o existente " +
                 "ou mande allowDuplicate=true com duplicateReason. Se uma ferramenta (prmake_tools) serve, prefira prmake_tool_run. tasks = lista " +
                 "[{ title, kind (branch|cherry-pick|code|pr|pr-review|devops-state|devops-action|pr-text|sql-query|sql-script|validation|comment|card-brief|" +
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
        // 0087: item rico — código citado com fotografia (popover) e sub-tarefas de código dizendo onde e por quê.
        if (await citations.CheckWorkAsync(c, new[] { request.Description }.Concat(request.Tasks.Select(t => t.Description)),
                request.Tasks.Select(t => (t.Title, t.Kind, t.Description)), ct) is { } incomplete)
            throw new ModelContextProtocol.McpException(incomplete);
        try
        {
            return await specialist.RunAsync(c, $"Criar o item de trabalho: {title}", description, async () =>
            {
                var created = await items.CreateAsync(c, request, origin, await ActorAsync(ct), ct);
                return (Serialize(new
                {
                    created.Number, created.Title, created.Status, created.Origin, created.Fingerprint,
                    tasks = created.Tasks.OrderBy(t => t.Order).Select(t => new { t.Number, t.Order, t.Kind, t.Title })
                }), $"item #{created.Number} criado com {created.Tasks.Count} sub-tarefa(s)");
            }, ct);
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
            return await specialist.RunAsync(c, $"Criar o item de trabalho pela ferramenta {key}", request.Title, async () =>
            {
                var created = await items.CreateAsync(c, request, origin, await ActorAsync(ct), ct);
                return (Serialize(new
                {
                    created.Number, created.Type, created.Title, created.Status, created.Origin, created.TemplateKey, created.Fingerprint,
                    tasks = created.Tasks.OrderBy(t => t.Order).Select(t => new { t.Number, t.Order, t.Kind, t.Title, t.Params })
                }), $"item #{created.Number} \"{created.Title}\" criado com {created.Tasks.Count} sub-tarefa(s)");
            }, ct);
        }
        catch (WorkItemDuplicateException d)
        {
            throw new McpException(
                $"Ja existe o item #{d.ExistingNumber} \"{d.ExistingTitle}\" ({d.ExistingStatus}) com o mesmo objetivo neste card — nao criei outro. " +
                $"Use o existente (prmake_work_item card {c} number {d.ExistingNumber}); se for de fato outro trabalho, chame de novo com " +
                "allowDuplicate=true e duplicateReason explicando a diferenca.", d);
        }
    });

    /// <summary>0092: chave da ferramenta da validação em ambiente (semeada pela <c>SeedEnvValidation0092</c>).</summary>
    public const string EnvValidationTemplate = "validar-em-ambiente";

    [McpServerTool(Name = "prmake_env_validation")]
    [Description("Cria o item de VALIDACAO EM AMBIENTE do card (ferramenta validar-em-ambiente): uma sub-tarefa env-validation por " +
                 "ambiente — o Claude entra no ambiente (navegador da maquina de quem executa, senha guardada so nela), segue o roteiro e " +
                 "grava a sequencia de prints como evidencia na sub-tarefa. environments = chaves de ValidationEnvironments (dev, qa, " +
                 "sandboxefeso, sandboxrt, performancetest, demo, gerdau, mondelez...) ou URL/nome de outro ambiente, separados por virgula; " +
                 "vazio = os sugeridos para o tipo do card. DEMO e producao: so navegar e consultar. Mesma checagem de duplicidade do " +
                 "prmake_tool_run. execute=true poe o item na fila do executor (so com pedido da pessoa).")]
    public Task<string> EnvValidation(string card,
        [Description("Ambientes separados por virgula (ex.: \"dev, qa, mondelez\"); vazio = os sugeridos")] string? environments = null,
        [Description("Sigla da planta (ex.: COR) — o usuario padrao vira o admin da planta")] string? plant = null,
        [Description("E-mail do usuario (opcional; padrao: o do ambiente ou o admin da planta)")] string? user = null,
        [Description("O que validar (roteiro curto; vazio = o que o card pede)")] string? scenario = null,
        [Description("Resultado esperado (opcional)")] string? expected = null,
        [Description("Titulo do item (opcional)")] string? title = null,
        [Description("true = ja coloca na fila do executor")] bool execute = false,
        [Description("Cria mesmo havendo item igual — exige duplicateReason")] bool allowDuplicate = false,
        string? duplicateReason = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        EnsureSpecialistCard(c);
        var answers = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        void Put(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) answers[key] = JsonSerializer.SerializeToElement(value.Trim());
        }
        Put("environments", environments);
        Put("plant", plant);
        Put("user", user);
        Put("scenario", scenario);
        CreateWorkItemRequest request;
        try
        {
            request = await tools.BuildCreateRequestAsync(c, new CreateFromTemplateRequest
            {
                TemplateKey = EnvValidationTemplate, Answers = answers, Title = title, AllowDuplicate = allowDuplicate, DuplicateReason = duplicateReason
            }, ct);
        }
        catch (WorkItemTemplateException e) when (e.Code == WorkItemTemplateException.NotFound)
        {
            throw new McpException($"A ferramenta {EnvValidationTemplate} nao esta no catalogo (WorkItemTemplates) — peca ao admin para restaurar o modelo semeado.", e);
        }
        catch (WorkItemTemplateException e)
        {
            throw new McpException(e.Missing.Count > 0 ? $"{e.Message} Faltando: {string.Join(", ", e.Missing)}." : e.Message, e);
        }
        if (!string.IsNullOrWhiteSpace(expected))
            foreach (var t in request.Tasks.Where(t => t.Kind == WorkTaskKind.EnvValidation))
                (t.Params ??= [])["expected"] = expected.Trim();
        var origin = CaseClaims.SpecialistOf(User) is not null ? WorkItemOrigin.Specialist : WorkItemOrigin.Template;
        try
        {
            return await specialist.RunAsync(c, "Criar o item de validacao em ambiente", request.Title, async () =>
            {
                var actor = await ActorAsync(ct);
                var created = await items.CreateAsync(c, request, origin, actor, ct);
                var status = created.Status;
                if (execute)
                    status = (await items.ExecuteAsync(created.Number, actor, ct)).Status;
                return (Serialize(new
                {
                    created.Number, created.Type, created.Title, status, created.Origin, created.TemplateKey, created.Fingerprint,
                    tasks = created.Tasks.OrderBy(t => t.Order).Select(t => new { t.Number, t.Order, t.Kind, t.Title, t.Params })
                }), $"item de validacao #{created.Number} criado com {created.Tasks.Count} ambiente(s){(execute ? " e na fila do executor" : string.Empty)}");
            }, ct);
        }
        catch (WorkItemDuplicateException d)
        {
            throw new McpException(
                $"Ja existe o item #{d.ExistingNumber} \"{d.ExistingTitle}\" ({d.ExistingStatus}) com a mesma validacao neste card — nao criei outro. " +
                $"Use o existente (prmake_work_item card {c} number {d.ExistingNumber}); se for de fato outra validacao, chame de novo com " +
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
        return await specialist.RunAsync(c, $"Editar o item de trabalho #{number}", title, async () =>
        {
            var updated = await items.UpdateAsync(number, new UpdateWorkItemRequest
            {
                Title = title ?? current.Title,
                Description = description ?? current.Description,
                Tasks = tasks is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } t ? ParseTasks(t) : null
            }, await ActorAsync(ct), ct);
            return (Serialize(new
            {
                updated.Number, updated.Title, updated.Status, updated.Fingerprint,
                tasks = updated.Tasks.OrderBy(x => x.Order).Select(x => new { x.Number, x.Order, x.Kind, x.Title, x.Status })
            }), $"item #{updated.Number} editado");
        }, ct);
    });

    [McpServerTool(Name = "prmake_work_item_cancel", Destructive = true)]
    [Description("Cancela um item de trabalho do card (para o pedido na fila, se houver). undo=true tambem desfaz o que ele ja fez " +
                 "(cada sub-tarefa concluida pelo 'undo' registrado). Concluido so com undo=true. Especialista do caso: so com pedido " +
                 "explicito da pessoa na conversa (confirme em uma linha se for ambiguo) e nunca item espelho do plano — o plano se " +
                 "pausa/cancela na tela.")]
    public Task<string> Cancel(string card, [Description("Numero do item (#n)")] long number,
        [Description("Tambem desfazer o que o item ja fez")] bool undo = false,
        [Description("Motivo (vai para o item e a Timeline)")] string? reason = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        EnsureSpecialistCard(c);
        var current = await items.GetAsync(number, false, ct);
        EnsureSameCard(current, c);
        if (specialist.IsSpecialist && current.PlanId is not null && current.Tasks.Any(t => t.StepKey is not null))
            throw new McpException($"O item #{number} espelha o plano — o Especialista do caso nao cancela o plano. Para parar, a pessoa " +
                                   "usa Pausar/Cancelar no menu do plano; para mudar, proponha com prmake_case_propose.");
        var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (specialist.Via is { } via) why = why is null ? via : $"{why} ({via})";
        return await specialist.RunAsync(c, $"{(undo ? "Cancelar e desfazer" : "Cancelar")} o item de trabalho #{number}", why, async () =>
        {
            var item = await items.CancelAsync(number, undo, why, await ActorAsync(ct), ct);
            return (Serialize(new { item.Number, item.Title, item.Status, item.CancelReason }), $"item #{item.Number} {item.Status}");
        }, ct);
    });

    [McpServerTool(Name = "prmake_work_item_execute")]
    [Description("Executa um item de trabalho do card (o mesmo play da Bancada): item avulso entra na fila do executor do dono; " +
                 "item ligado ao plano retoma o plano. Item cancelado/concluido nao executa (crie outro). Especialista do caso: so " +
                 "com pedido da pessoa na conversa.")]
    public Task<string> Execute(string card, [Description("Numero do item (#n)")] long number, CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        EnsureSpecialistCard(c);
        var current = await items.GetAsync(number, false, ct);
        EnsureSameCard(current, c);
        return await specialist.RunAsync(c, $"Executar o item de trabalho #{number}", current.Title, async () =>
        {
            var item = await items.ExecuteAsync(number, await ActorAsync(ct), ct);
            return (Serialize(new { item.Number, item.Title, item.Status, item.PlanId }),
                item.PlanId is null ? $"item #{item.Number} na fila do executor" : $"item #{item.Number}: plano retomado");
        }, ct);
    });

    [McpServerTool(Name = "prmake_analysis_items")]
    [Description("Cria os itens de trabalho a partir da analise ja respondida que terminou sem plano de implementacao/correcao (ex.: a " +
                 "pessoa respondeu 'so a analise por enquanto'): o mesmo botao 'Criar itens da analise' da Bancada — o Claude do tratamento " +
                 "monta o plano (que vira o item de trabalho) na maquina de quem pediu. start=true cria e ja executa; start=false cria e deixa " +
                 "pausado (a pessoa inicia pelo Executar do item). Especialista do caso: so com pedido da pessoa na conversa.")]
    public Task<string> AnalysisItems(string card,
        [Description("true = criar e iniciar agora; false = criar sem iniciar")] bool start,
        [Description("O que a pessoa acrescentou (opcional)")] string? note = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        EnsureSpecialistCard(c);
        return await specialist.RunAsync(c, start ? "Criar os itens da analise e iniciar" : "Criar os itens da analise sem iniciar", null, async () =>
        {
            var request = await queue.RequestDeliveryAsync(c, start, note, await ActorAsync(ct), ct);
            return (Serialize(new { request.Id, request.Status, start }),
                start ? "itens da analise: o Claude do tratamento monta o plano e executa" : "itens da analise: o Claude do tratamento monta o plano e deixa pausado");
        }, ct);
    });

    [McpServerTool(Name = "prmake_work_task")]
    [Description("Registra o andamento de uma sub-tarefa de item de trabalho: action start | complete | fail | wait | skip | revert | resolve. " +
                 "result = markdown curto (em fail/wait: o motivo / o que a pessoa precisa fazer); output = fatos (pr, number, repository, " +
                 "branch, target, commit, commits, created, from); undo = {action, params} de como desfazer (ao concluir); links = " +
                 "[{kind: pr|branch|commit|doc, title, url}] para o item. resolve = \"Ja resolvi\" da pessoa numa sub-tarefa do Claude " +
                 "em espera (result = o que ela fez): volta a pendente e o item e retomado.")]
    public Task<string> TaskAction(string card, [Description("Numero da sub-tarefa (#n)")] long taskNumber,
        [Description("start | complete | fail | wait | skip | revert | resolve")] string action,
        string? result = null,
        [Description("Objeto JSON (ou o JSON em texto) com as saidas")] JsonElement? output = null,
        [Description("{action, params} (ou o JSON em texto) — como desfazer")] JsonElement? undo = null,
        [Description("[{kind, title, url}] (ou o JSON em texto) — links para o item")] JsonElement? links = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        EnsureSpecialistCard(c);
        var owner = (await items.ListAsync(c, ct)).FirstOrDefault(i => i.Tasks.Any(t => t.Number == taskNumber))
                    ?? throw new McpException($"A sub-tarefa #{taskNumber} nao e de um item do card {c}.");
        var verb = (action ?? string.Empty).Trim().ToLowerInvariant();
        // "Ja resolvi" e acao da pessoa (repassada pelo especialista), nao mudanca do plano.
        if (verb != "resolve" && specialist.IsSpecialist && owner.PlanId is not null && owner.Tasks.First(t => t.Number == taskNumber).StepKey is not null)
            throw new McpException($"A sub-tarefa #{taskNumber} espelha uma etapa do plano — o Especialista do caso nao altera o plano. " +
                                   "Proponha a mudanca com prmake_case_propose.");
        var request = new WorkTaskActionRequest
        {
            Result = result,
            Output = AsObject(output),
            Undo = AsObject(undo) is { } u ? ParseUndo(u) : null,
            Links = AsJson(links) is { ValueKind: JsonValueKind.Array } l ? l.Deserialize<List<WorkItemLink>>(Json) : null
        };
        return await specialist.RunAsync(c, $"Sub-tarefa #{taskNumber} do item #{owner.Number}: {verb}", result, async () =>
        {
            var item = verb == "resolve"
                ? await items.ResolveTaskAsync(taskNumber, result, await ActorAsync(ct), ct)
                : await items.TaskActionAsync(taskNumber, verb, request, await ActorAsync(ct), ct);
            var task = item.Tasks.FirstOrDefault(t => t.Number == taskNumber);
            return (Serialize(new
            {
                item = new { item.Number, item.Status, progress = $"{item.Progress.Done}/{item.Progress.Total}" },
                task = task is null ? null : new { task.Number, task.Status },
                next = item.Tasks.OrderBy(t => t.Order).FirstOrDefault(t => t.Status is WorkTaskStatus.Pending or WorkTaskStatus.Waiting) is { } n
                    ? new { n.Number, n.Title, n.Kind, n.Status }
                    : null
            }), $"sub-tarefa #{taskNumber}: {task?.Status ?? verb}");
        }, ct);
    });

    [McpServerTool(Name = "prmake_work_deliverable")]
    [Description("0091: registra o que a sub-tarefa GEROU (aparece no modal da sub-tarefa e na aba Entregas do item). kind: pr | branch | " +
                 "commit | ticket | pr-text | devops-state | comment | timeline | hours | notify | doc. Mesma coisa de novo (tipo + repositorio + " +
                 "branch/numero/url) atualiza. PR aberto pelo PRMake, acoes do DevOps e Timeline ja sao registrados sozinhos; codigo alterado " +
                 "vai pelo prmake-plan.sh deliver-code (o diff sai do worktree, nao passe diff aqui). data = extras (ex.: {from, to}, {system}).")]
    public Task<string> Deliverable(string card, [Description("Numero da sub-tarefa (#n)")] long taskNumber, string kind,
        string? title = null, string? url = null, [Description("pr: open|merged|closed|draft; ticket: open|resolved|closed")] string? status = null,
        string? repository = null, string? branch = null, [Description("pr: destino; branch: base")] string? target = null,
        int? number = null, string? sha = null, [Description("Texto curto (ate 600)")] string? text = null,
        [Description("Objeto JSON (ou o JSON em texto) com extras")] JsonElement? data = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        if (specialist.IsSpecialist)
            throw new McpException("As entregas sao registradas pela execucao da sub-tarefa, nao pelo Especialista do caso.");
        var owner = (await items.ListAsync(c, ct)).FirstOrDefault(i => i.Tasks.Any(t => t.Number == taskNumber))
                    ?? throw new McpException($"A sub-tarefa #{taskNumber} nao e de um item do card {c}.");
        var saved = await deliverables.RecordAsync(taskNumber, new WorkDeliverableInput
        {
            Kind = kind, Title = title, Url = url, Status = status, Repository = repository, Branch = branch, Target = target,
            Number = number, Sha = sha, Text = text, Data = AsObject(data),
        }, await ActorAsync(ct), ct);
        return Serialize(new { item = owner.Number, deliverable = new { saved.Id, saved.TaskNumber, saved.Kind, saved.Title, saved.Url } });
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
        if (specialist.Actor() is { } requested)
            return requested;
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
