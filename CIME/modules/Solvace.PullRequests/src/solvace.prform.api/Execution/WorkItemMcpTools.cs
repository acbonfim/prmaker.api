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
/// lista/cria/edita itens do caso (<see cref="CaseSpecialistMiddleware.IsAllowed"/>) e (r5) cancela,
/// executa e registra o andamento de itens a pedido de uma pessoa — nunca de sub-tarefa espelho do plano (no item espelho, só
/// acrescenta sub-tarefa nova: <c>prmake_work_item_add_task</c> vira etapa pendente do plano). Card 78680: em qualquer item,
/// move/pausa/continua/cancela uma sub-tarefa (<c>prmake_work_task_control</c>) sem cancelar e recriar o item. Item avulso já começado ou
/// fechado: reabre (<c>prmake_work_item_reopen</c>, refazendo sub-tarefas), acrescenta sub-tarefa e edita sub-tarefa
/// pendente (<c>prmake_work_task_update</c>). Duplicata (mesmo fingerprint) volta como mensagem clara com o item existente
/// e como forçar.
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
    IWorkItemDeliverableApplication deliverables,
    IEpicApplication epics,
    IEpicStoryApplication stories,
    DeskProfileService profile)
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
        var markdown = SkillsRulesService.Markdown(r, await tools.RepositoryPlansAsync(r, ct)) + await rules.GovernanceMarkdownAsync(ct)
                       + await rules.StandardsMarkdownAsync(r.Card, await tools.CardRepositoriesAsync(r.Card, ct), ct);
        // 0099 (ajustes 2): no épico, o perfil de quem pede (o item de gestão roda na máquina de quem agiu).
        if (SkillsRulesService.IsEpic(r) && http.HttpContext?.User is { } user)
            markdown += await profile.MarkdownAsync(user, ct);
        return markdown;
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
                tasks = i.Tasks.OrderBy(t => t.Order).Select(t => new
                {
                    t.Number, t.Order, t.Kind, t.Title, t.Status, t.Executor,
                    // Card 78680: pausada pela pessoa e de quem depende (o que depende de outra nao passa na frente dela).
                    held = t.Held ? true : (bool?)null,
                    dependsOn = t.Controls is { DependsOn.Count: > 0 } c ? c.DependsOn : null,
                })
            })
        });
    });

    [McpServerTool(Name = "prmake_work_item", ReadOnly = true)]
    [Description("Detalhe de um item de trabalho: sub-tarefas com params, resultado, saidas (output), como desfazer (undo), " +
                 "links, pedido da fila e o historico recente. Aceita o #n citado na conversa: numero de sub-tarefa devolve o item dono " +
                 "com matchedTaskNumber; item de outro card (citado como referencia) vem com otherCard=true — para altera-lo (so a pedido de " +
                 "uma pessoa) chame as ferramentas com o card dele.")]
    public Task<string> Get(string card, [Description("Numero do item ou da sub-tarefa (#n)")] long number, CancellationToken ct = default) => Safe(async () =>
    {
        WorkItemResponse item;
        try
        {
            item = await items.ResolveAsync(number, true, ct);
        }
        catch (ExecutionPlanNotFoundException)
        {
            // 0099: a numeração é a mesma dos épicos — #n de épico devolve o épico (detalhe em prmake_epic).
            if (await epics.FindAsync(number, ct) is { } epic)
                return Serialize(new { kind = "epic", note = $"#{number} é o épico {epic.Key} da Mesa de gestão — detalhe com prmake_epic({number}); itens do épico com card {epic.Key}.", epic });
            // Ajustes 2: #n de história/bug escrito na Mesa (rascunho do épico ou já convertido em card).
            if (await stories.FindAsync(number, ct) is not { } story) throw;
            var where = story.CardId is { } cardId ? $"já virou o card {cardId}" : "rascunho, ainda não é card";
            return Serialize(new { kind = "story", note = $"#{number} é uma história/bug do épico {story.EpicKey} ({where}) — prmake_epic_stories({story.EpicNumber}).", story });
        }
        var otherCard = !string.Equals(item.CardNumber, RequireCard(card), StringComparison.Ordinal);
        return otherCard ? Serialize(new { otherCard = true, note = $"Item do card {item.CardNumber} — referencia; para altera-lo (so a pedido de uma pessoa), use card {item.CardNumber} nas ferramentas.", item }) : Serialize(item);
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
                 "grava a sequencia de prints como evidencia na sub-tarefa; no fim, uma sub-tarefa escreve o comentario com as evidencias " +
                 "(arquivo do item) e so o publica na discussion do card com discussion=\"sim\". environments = chaves de ValidationEnvironments (dev, qa, " +
                 "sandboxefeso, sandboxrt, performancetest, demo, gerdau, mondelez...) ou URL/nome de outro ambiente, separados por virgula; " +
                 "vazio = os sugeridos para o tipo do card. DEMO e producao: so navegar e consultar. Mesma checagem de duplicidade do " +
                 "prmake_tool_run. execute=true poe o item na fila do executor (so com pedido da pessoa).")]
    public Task<string> EnvValidation(string card,
        [Description("Ambientes separados por virgula (ex.: \"dev, qa, mondelez\"); vazio = os sugeridos")] string? environments = null,
        [Description("Sigla da planta (ex.: COR) — o Claude troca para ela na tela; o admin da planta so entra se nao houver outro usuario com senha na maquina")] string? plant = null,
        [Description("E-mail do usuario (opcional; padrao: quem ja tem senha gravada na maquina que executa — ex.: admin@ewcm.com.br, que entra em qualquer planta —, senao o do ambiente ou o admin da planta). Nao pergunte a pessoa")] string? user = null,
        [Description("O que validar (roteiro curto; vazio = o que o card pede)")] string? scenario = null,
        [Description("Resultado esperado (opcional)")] string? expected = null,
        [Description("Publicar o comentario com as evidencias na discussion do card: \"sim\" so quando a pessoa pediu; vazio/\"nao\" = " +
                     "o texto fica so nos arquivos do item")] string? discussion = null,
        [Description("Titulo do item (opcional)")] string? title = null,
        [Description("true = ja coloca na fila do executor")] bool execute = false,
        [Description("Cria mesmo havendo item igual — exige duplicateReason")] bool allowDuplicate = false,
        string? duplicateReason = null,
        CancellationToken ct = default) => Safe(() => CreateEnvItemAsync(card, EnvValidationTemplate, WorkTaskKind.EnvValidation,
        new Dictionary<string, string?>
        {
            ["environments"] = environments, ["plant"] = plant, ["user"] = user, ["scenario"] = scenario, ["discussion"] = YesNo(discussion)
        },
        expected, title, execute, allowDuplicate, duplicateReason, "validacao em ambiente", ct));

    /// <summary>Resposta sim/não das ferramentas (opções "não"/"sim" do modelo); outro valor ou vazio = o padrão do modelo.</summary>
    public static string? YesNo(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "sim" or "s" or "yes" or "y" or "true" => "sim",
        "não" or "nao" or "n" or "no" or "false" => "não",
        _ => null
    };

    /// <summary>0093: chave da ferramenta da reprodução do bug no ambiente (semeada pela <c>SeedEnvReproduction0093</c>).</summary>
    public const string EnvReproductionTemplate = "reproduzir-bug-no-ambiente";

    [McpServerTool(Name = "prmake_env_reproduction")]
    [Description("Cria o item de REPRODUCAO DO BUG NO AMBIENTE (ferramenta reproduzir-bug-no-ambiente, so Bug): uma sub-tarefa " +
                 "env-reproduction por ambiente — o Claude entra no ambiente (navegador da maquina de quem executa, senha guardada so nela), " +
                 "segue os passos para reproduzir do card e grava os prints do erro (ou de que nao reproduziu) como evidencia na sub-tarefa. " +
                 "environments vazio ou \"cliente\" = o ambiente relatado pelo cliente no card (padrao); outro ambiente e opcional: chaves de " +
                 "ValidationEnvironments (dev, qa, sandboxefeso, sandboxrt, gerdau, mondelez...) ou URL, separados por virgula (ex.: " +
                 "\"cliente, qa\"). DEMO e producao: so navegar e consultar (o passo que grava dados vai para o sandbox do cliente). Mesma " +
                 "checagem de duplicidade do prmake_tool_run. execute=true poe o item na fila do executor (so com pedido da pessoa).")]
    public Task<string> EnvReproduction(string card,
        [Description("Ambientes separados por virgula; vazio ou \"cliente\" = o relatado pelo cliente no card")] string? environments = null,
        [Description("Sigla da planta (ex.: COR) — vazio = a do card; o Claude troca para ela na tela; o admin da planta so entra se nao houver outro usuario com senha na maquina")] string? plant = null,
        [Description("E-mail do usuario (opcional; padrao: quem ja tem senha gravada na maquina que executa — ex.: admin@ewcm.com.br, que entra em qualquer planta —, senao o do ambiente ou o admin da planta). Nao pergunte a pessoa")] string? user = null,
        [Description("Passos para reproduzir (vazio = os do card)")] string? steps = null,
        [Description("Comportamento esperado (opcional)")] string? expected = null,
        [Description("Titulo do item (opcional)")] string? title = null,
        [Description("true = ja coloca na fila do executor")] bool execute = false,
        [Description("Cria mesmo havendo item igual — exige duplicateReason")] bool allowDuplicate = false,
        string? duplicateReason = null,
        CancellationToken ct = default) => Safe(() => CreateEnvItemAsync(card, EnvReproductionTemplate, WorkTaskKind.EnvReproduction,
        new Dictionary<string, string?> { ["environments"] = environments, ["plant"] = plant, ["user"] = user, ["steps"] = steps },
        expected, title, execute, allowDuplicate, duplicateReason, "reproducao do bug no ambiente", ct));

    /// <summary>0092/0093: item de validação/reprodução pela ferramenta (uma sub-tarefa por ambiente), com execução opcional.</summary>
    private async Task<string> CreateEnvItemAsync(string card, string templateKey, string kind, IReadOnlyDictionary<string, string?> given,
        string? expected, string? title, bool execute, bool allowDuplicate, string? duplicateReason, string what, CancellationToken ct)
    {
        var c = RequireCard(card);
        var answers = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in given)
            if (!string.IsNullOrWhiteSpace(value))
                answers[key] = JsonSerializer.SerializeToElement(value.Trim());
        CreateWorkItemRequest request;
        try
        {
            request = await tools.BuildCreateRequestAsync(c, new CreateFromTemplateRequest
            {
                TemplateKey = templateKey, Answers = answers, Title = title, AllowDuplicate = allowDuplicate, DuplicateReason = duplicateReason
            }, ct);
        }
        catch (WorkItemTemplateException e) when (e.Code == WorkItemTemplateException.NotFound)
        {
            throw new McpException($"A ferramenta {templateKey} nao esta no catalogo (WorkItemTemplates) — peca ao admin para restaurar o modelo semeado.", e);
        }
        catch (WorkItemTemplateException e)
        {
            throw new McpException(e.Missing.Count > 0 ? $"{e.Message} Faltando: {string.Join(", ", e.Missing)}." : e.Message, e);
        }
        if (!string.IsNullOrWhiteSpace(expected))
            foreach (var t in request.Tasks.Where(t => t.Kind == kind))
                (t.Params ??= [])["expected"] = expected.Trim();
        var origin = CaseClaims.SpecialistOf(User) is not null ? WorkItemOrigin.Specialist : WorkItemOrigin.Template;
        try
        {
            return await specialist.RunAsync(c, $"Criar o item de {what}", request.Title, async () =>
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
                }), $"item de {what} #{created.Number} criado com {created.Tasks.Count(t => t.Kind == kind)} ambiente(s){(execute ? " e na fila do executor" : string.Empty)}");
            }, ct);
        }
        catch (WorkItemDuplicateException d)
        {
            throw new McpException(
                $"Ja existe o item #{d.ExistingNumber} \"{d.ExistingTitle}\" ({d.ExistingStatus}) com o mesmo objetivo neste card — nao criei outro. " +
                $"Use o existente (prmake_work_item card {c} number {d.ExistingNumber}); se for de fato outro trabalho, chame de novo com " +
                "allowDuplicate=true e duplicateReason explicando a diferenca.", d);
        }
    }

    [McpServerTool(Name = "prmake_work_item_update")]
    [Description("Edita um item de trabalho: titulo e descricao (criterios de aceite) em QUALQUER item nao desfeito, mesmo ja " +
                 "comecado ou concluido; a lista de sub-tarefas inteira (tasks substitui a lista; mande 'number' de uma sub-tarefa " +
                 "existente para mante-la) so no item ainda pendente. Item ja iniciado: sub-tarefa a mais com prmake_work_item_add_task, " +
                 "instrucao de uma sub-tarefa nao feita com prmake_work_task_update, ordem com prmake_work_task_move.")]
    public Task<string> Update(string card, [Description("Numero do item (#n)")] long number, string? title = null, string? description = null,
        [Description("Nova lista de sub-tarefas (opcional): [{number?, title, kind, description?, executor?, params?}]")] JsonElement? tasks = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        var current = await items.GetAsync(number, false, ct);
        EnsureSameCard(current, c);
        var hasTasks = tasks is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) };
        if (hasTasks && current.PlanId is not null && current.Tasks.Any(t => t.StepKey is not null) && !current.Editable)
            throw new McpException($"O item #{number} espelha o plano e ja comecou — sub-tarefa a mais: prmake_work_item_add_task(card {c}, " +
                                   $"number {number}, title, description); texto de uma etapa: prmake_step. Titulo/descricao do item: mande " +
                                   "sem tasks.");
        if (hasTasks && current.PlanId is null && !current.Editable)
            throw new McpException($"O item #{number} ja comecou ou terminou ({current.Status}) — a lista de sub-tarefas nao se troca inteira. " +
                                   $"Sub-tarefa a mais: prmake_work_item_add_task(card {c}, number {number}, ...); instrucao de uma nao feita: " +
                                   "prmake_work_task_update; ordem: prmake_work_task_move; ja feita: prmake_work_item_reopen com redo. " +
                                   "Titulo/descricao do item: mande sem tasks.");
        return await specialist.RunAsync(c, $"Editar o item de trabalho #{number}", title, async () =>
        {
            var updated = await items.UpdateAsync(number, new UpdateWorkItemRequest
            {
                Title = title ?? current.Title,
                Description = description ?? current.Description,
                Tasks = hasTasks ? ParseTasks(tasks!.Value) : null
            }, await ActorAsync(ct), ct);
            return (Serialize(new
            {
                updated.Number, updated.Title, updated.Status, updated.Fingerprint,
                tasks = updated.Tasks.OrderBy(x => x.Order).Select(x => new { x.Number, x.Order, x.Kind, x.Title, x.Status })
            }), $"item #{updated.Number} editado");
        }, ct);
    });

    [McpServerTool(Name = "prmake_work_item_add_task")]
    [Description("Acrescenta uma sub-tarefa a um item de trabalho ja existente. Item que ESPELHA o plano (analise/correcao em " +
                 "andamento): vira etapa nova, pendente, do plano — sem mudar as outras — antes da etapa em andamento/espera (ou de " +
                 "'before', chave de etapa ainda nao terminada); o Claude do tratamento a faz antes de seguir. Item AVULSO aberto " +
                 "(pendente, em espera ou com falha, mesmo ja comecado): a sub-tarefa entra no fim (ou antes da sub-tarefa ainda nao " +
                 "feita 'before' = #n — em espera/com falha, ela volta a pendente e roda depois da nova). Item avulso concluido/cancelado: reabra antes com prmake_work_item_reopen. execute=true ja executa " +
                 "(retoma o plano / poe o item na fila). Especialista do caso: so com pedido da pessoa na conversa.")]
    public Task<string> AddTask(string card, [Description("Numero do item (#n)")] long number,
        [Description("Titulo curto da sub-tarefa (o que fazer)")] string title,
        [Description("O que fazer e para que (objetivo, fontes, o que confirmar, evidencias exigidas) — o Claude que executa segue este texto")] string description,
        [Description("Tipo da sub-tarefa (catalogo WorkItemTaskKinds: research, sql-query, env-reproduction, env-validation, code, pr, task...; no plano, sem ele o PRMake deduz)")] string? kind = null,
        [Description("Item espelho do plano: chave da etapa antes da qual entra (padrao: a em andamento/espera). Item avulso: numero da sub-tarefa ainda nao feita (pendente, em espera ou com falha) antes da qual entra (padrao: no fim)")] string? before = null,
        [Description("first = no inicio (antes da primeira ainda nao feita — ex.: 'antes de tudo, peca o HAR no card'); last = no fim. Sem before nem position: item espelho = antes da etapa atual; avulso = no fim")] string? position = null,
        [Description("Item avulso: claude (padrao) ou user")] string? executor = null,
        [Description("Item avulso: parametros da sub-tarefa {repository, branch, environment, steps, expected, ...} (objeto ou JSON em texto)")] JsonElement? @params = null,
        [Description("Executar agora (a pessoa pediu para ja fazer)")] bool execute = false,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        var current = await items.GetAsync(number, false, ct);
        EnsureSameCard(current, c);
        var why = specialist.Via is { } via ? $"Acrescentada {via}." : null;
        var text = why is null || description.Contains(why, StringComparison.Ordinal) ? description : $"{description}\n\n{why}";
        if (current.PlanId is not { } planId || !current.Tasks.Any(t => t.StepKey is not null))
        {
            if (current.PlanId is not null)
                throw new McpException($"O item #{number} esta ligado ao plano sem espelhar as etapas — a mudanca vai por proposta (ajusta o mesmo " +
                                       "plano, prmake_case_propose).");
            if (current.Status is WorkItemStatus.Completed or WorkItemStatus.Cancelled)
                throw new McpException($"O item #{number} esta {current.Status} — reabra antes com prmake_work_item_reopen(card {c}, number " +
                                       $"{number}, reason) e chame de novo (ou, para refazer sub-tarefas ja feitas, informe redo no reopen).");
            long? beforeTask = null;
            if (!string.IsNullOrWhiteSpace(before))
                beforeTask = long.TryParse(before.Trim().TrimStart('#'), out var b) ? b
                    : throw new McpException("Item avulso: 'before' e o numero (#n) da sub-tarefa ainda nao feita antes da qual entra.");
            var input = new WorkTaskInput
            {
                Title = title, Description = text, Kind = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim(),
                Executor = executor, Params = AsObject(@params) is { } po ? ToStringMap(po) : null
            };
            if (await citations.CheckWorkAsync(c, [input.Description], [(input.Title, input.Kind, input.Description)], ct) is { } incomplete)
                throw new McpException(incomplete);
            return await specialist.RunAsync(c, $"Acrescentar sub-tarefa ao item #{number}", title, async () =>
            {
                var item = await items.AddTasksAsync(number, new AddWorkTasksRequest { Tasks = [input], Before = beforeTask, Position = position, Execute = execute },
                    await ActorAsync(ct), ct);
                var known = current.Tasks.Select(t => t.Number).ToHashSet();
                var added = item.Tasks.Where(t => !known.Contains(t.Number)).OrderBy(t => t.Order).ToList();
                return (Serialize(new
                {
                    item.Number, item.Title, item.Status, taskNumbers = added.Select(t => t.Number),
                    tasks = item.Tasks.OrderBy(t => t.Order).Select(t => new { t.Number, t.Order, t.Kind, t.Title, t.Status })
                }), $"sub-tarefa {string.Join(", ", added.Select(t => $"#{t.Number}"))} acrescentada ao item #{item.Number}" +
                    $"{(execute ? " e item na fila" : string.Empty)}");
            }, ct);
        }

        var plan = await plans.GetAsync(planId, ct);
        var definition = new ExecutionStepDefinition
        {
            Key = NewStepKey(title, plan.Steps.Select(s => s.Key)),
            Title = title,
            Description = text,
            Executor = ExecutionExecutor.Claude,
            TaskKind = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim(),
        };
        return await specialist.RunAsync(c, $"Acrescentar sub-tarefa ao item #{number}", title, async () =>
        {
            var actor = await ActorAsync(ct);
            var step = await plans.AddStepAsync(planId, definition, before, actor, ct, position);
            var item = await items.GetAsync(number, false, ct);
            var task = item.Tasks.FirstOrDefault(t => t.StepKey == step.Key);
            if (execute)
                item = await items.ExecuteAsync(number, actor, ct);
            return (Serialize(new
            {
                item.Number, item.Title, item.Status, step = step.Key, taskNumber = task?.Number,
                tasks = item.Tasks.OrderBy(t => t.Order).Select(t => new { t.Number, t.Order, t.Title, t.Status })
            }), $"sub-tarefa {(task is null ? string.Empty : $"#{task.Number} ")}acrescentada ao item #{item.Number}" +
                $"{(execute ? " e plano retomado" : string.Empty)}");
        }, ct);
    });

    [McpServerTool(Name = "prmake_work_task_redo")]
    [Description("Card 78794: REFAZER UMA ENTREGA JA FEITA — script recusado pela infra/DBA, chamado ou texto corrigido depois de concluido. " +
                 "Use SEMPRE que voce (ou a pessoa) refizer na conversa algo que uma sub-tarefa concluida entregou; nunca so regrave o arquivo " +
                 "nem mande a versao nova so na conversa. Item espelho do plano: a sub-tarefa nova entra LOGO DEPOIS da refeita, ja " +
                 "concluida, com o motivo, o resultado (o que mudou) e os arquivos novos (files; a versao anterior de mesmo nome fica " +
                 "como <nome>.anterior). A refeita ganha 'Refeita na #n' no resultado. As que dependiam dela (ainda nao feitas) passam " +
                 "a depender da nova e ganham um aviso na instrucao. Item avulso: reabra com prmake_work_item_reopen(redo: [n]). " +
                 "Especialista do caso: so com pedido da pessoa na conversa.")]
    public Task<string> RedoTask(string card,
        [Description("Numero da sub-tarefa CONCLUIDA que foi refeita (#n)")] long taskNumber,
        [Description("Por que foi refeita (ex.: 'infra recusou: erro 229 no SELECT em msdb.dbo.sysschedules; agenda 10 compartilhada')")] string reason,
        [Description("Titulo curto da sub-tarefa nova (ex.: 'Refazer o script da agenda do job (recusado pela infra)')")] string title,
        [Description("Resultado em markdown: o que mudou em relacao a entrega anterior, os arquivos novos e o que a pessoa faz agora")] string result,
        [Description("Arquivos novos da entrega: [{name, content, kind?}] (ou o JSON em texto) — mesmo nome substitui e guarda a versao anterior")] JsonElement? files = null,
        [Description("O que a sub-tarefa nova fez (opcional; padrao: o motivo)")] string? description = null,
        [Description("Tipo da sub-tarefa (catalogo; padrao: o da refeita)")] string? kind = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        var owner = (await items.ListAsync(c, ct)).FirstOrDefault(i => i.Tasks.Any(t => t.Number == taskNumber))
                    ?? throw new McpException($"A sub-tarefa #{taskNumber} nao e de um item do card {c}.");
        var redone = owner.Tasks.First(t => t.Number == taskNumber);
        if (owner.PlanId is not { } planId || redone.StepKey is null)
            throw new McpException($"O item #{owner.Number} e avulso — refaca reabrindo: prmake_work_item_reopen(card {c}, number {owner.Number}, " +
                                   $"reason, redo: [{taskNumber}, <as que dependem dela>]) e, se a instrucao mudou, prmake_work_task_update.");
        if (string.IsNullOrWhiteSpace(reason)) throw new McpException("Informe o motivo (reason).");
        if (string.IsNullOrWhiteSpace(title)) throw new McpException("Informe o titulo da sub-tarefa nova.");

        var uploads = new List<ExecutionArtifactUpload>();
        if (AsJson(files) is { } list)
        {
            if (list.ValueKind != JsonValueKind.Array)
                throw new McpException("files deve ser uma lista [{name, content, kind?}].");
            foreach (var f in list.EnumerateArray())
            {
                var name = f.ValueKind == JsonValueKind.Object ? Str(f, "name") : null;
                var content = f.ValueKind == JsonValueKind.Object ? Str(f, "content") : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(content))
                    throw new McpException("Cada arquivo precisa de name e content.");
                var fileName = Path.GetFileName(name.Trim().Replace('\\', '/'));
                uploads.Add(new ExecutionArtifactUpload(fileName, Str(f, "kind"), null, null,
                    solvace.prform.Controllers.ExecutionPlanController.ResolveContentType(fileName, "text/plain; charset=utf-8"),
                    System.Text.Encoding.UTF8.GetBytes(content)));
            }
        }

        var plan = await plans.GetAsync(planId, ct);
        var definition = new ExecutionStepDefinition
        {
            Key = NewStepKey(title, plan.Steps.Select(s => s.Key)),
            Title = title,
            Description = string.IsNullOrWhiteSpace(description) ? $"Refaz a #{taskNumber} ({redone.Title}): {reason.Trim()}" : description,
            Executor = ExecutionExecutor.Claude,
            TaskKind = string.IsNullOrWhiteSpace(kind) ? redone.Kind : kind.Trim(),
        };
        return await specialist.RunAsync(c, $"Refazer a sub-tarefa #{taskNumber} do item #{owner.Number}", reason, async () =>
        {
            var actor = await ActorAsync(ct);
            var step = await plans.RedoStepAsync(planId, redone.StepKey, definition, reason, result, uploads, actor, ct);
            var item = await items.GetAsync(owner.Number, false, ct);
            var task = item.Tasks.FirstOrDefault(t => t.StepKey == step.Key);
            var warned = item.Tasks.Where(t => t.Status is not (WorkTaskStatus.Completed or WorkTaskStatus.Skipped or WorkTaskStatus.Cancelled) && t.Number != task?.Number
                                               && t.Description?.Contains("**Entrega refeita:**", StringComparison.Ordinal) == true)
                .Select(t => t.Number).ToList();
            return (Serialize(new
            {
                item.Number, item.Status, step = step.Key, taskNumber = task?.Number, redone = taskNumber, warned,
                tasks = item.Tasks.OrderBy(t => t.Order).Select(t => new { t.Number, t.Order, t.Title, t.Status })
            }), $"#{taskNumber} refeita na {(task is null ? "sub-tarefa nova" : $"#{task.Number}")} do item #{item.Number}" +
                (warned.Count > 0 ? $"; avisadas: {string.Join(", ", warned.Select(n => $"#{n}"))}" : string.Empty));
        }, ct);
    });

    [McpServerTool(Name = "prmake_work_item_reopen")]
    [Description("Reabre um item de trabalho AVULSO concluido, cancelado ou com falha para refazer ou acrescentar trabalho. redo = " +
                 "sub-tarefas (#n) que voltam a pendente para serem refeitas (o resultado anterior fica, com o motivo no fim; arquivos e " +
                 "prints ja gravados ficam no item) — inclua as que dependem delas (ex.: refazer a reproducao e a proposta que a usa). " +
                 "Sem redo: so reabre, para acrescentar sub-tarefa com prmake_work_item_add_task. Item cancelado: as sub-tarefas " +
                 "canceladas voltam a pendente. execute=true ja poe na fila (precisa de sub-tarefa pendente). Item do plano (analise/" +
                 "correcao) nao reabre: muda pelo plano. Especialista do caso: so com pedido da pessoa na conversa.")]
    public Task<string> Reopen(string card, [Description("Numero do item (#n)")] long number,
        [Description("Motivo (vai para o item, as sub-tarefas refeitas e a Timeline)")] string? reason = null,
        [Description("Sub-tarefas a refazer: lista de numeros [232, 233] (ou o JSON em texto)")] JsonElement? redo = null,
        [Description("Executar logo depois de reabrir")] bool execute = false,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        var current = await items.GetAsync(number, false, ct);
        EnsureSameCard(current, c);
        var numbers = new List<long>();
        if (AsJson(redo) is { } r)
        {
            if (r.ValueKind == JsonValueKind.Number) numbers.Add(r.GetInt64());
            else if (r.ValueKind == JsonValueKind.Array)
                foreach (var n in r.EnumerateArray())
                    numbers.Add(n.ValueKind == JsonValueKind.Number ? n.GetInt64()
                        : long.TryParse(n.GetString()?.Trim().TrimStart('#'), out var v) ? v
                        : throw new McpException("redo: lista de numeros de sub-tarefa, ex.: [232, 233]."));
            else throw new McpException("redo: lista de numeros de sub-tarefa, ex.: [232, 233].");
        }
        var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (specialist.Via is { } via) why = why is null ? via : $"{why} ({via})";
        return await specialist.RunAsync(c, $"Reabrir o item de trabalho #{number}", why, async () =>
        {
            var item = await items.ReopenAsync(number, new ReopenWorkItemRequest { Redo = numbers, Reason = why, Execute = execute },
                await ActorAsync(ct), ct);
            return (Serialize(new
            {
                item.Number, item.Title, item.Status,
                tasks = item.Tasks.OrderBy(t => t.Order).Select(t => new { t.Number, t.Order, t.Kind, t.Title, t.Status })
            }), $"item #{item.Number} reaberto{(numbers.Count == 0 ? string.Empty : $" (refazer {string.Join(", ", numbers.Select(n => $"#{n}"))})")}" +
                $"{(execute ? " e na fila" : string.Empty)}");
        }, ct);
    });

    [McpServerTool(Name = "prmake_work_task_update")]
    [Description("Edita uma sub-tarefa AINDA NAO FEITA de um item avulso aberto (o item pode ja ter comecado): pendente, em espera " +
                 "ou com falha — essas duas voltam a pendente para refazer com a instrucao nova. Titulo, descricao, kind, executor e/ou " +
                 "params (o que nao for informado fica). Ja feita/pulada: reabra o item com ela em redo (prmake_work_item_reopen) e " +
                 "edite em seguida. execute=true ja poe o item na fila. Sub-tarefa espelho do plano muda pelo plano (prmake_step). " +
                 "Especialista do caso: so com pedido da pessoa na conversa.")]
    public Task<string> UpdateTask(string card, [Description("Numero da sub-tarefa (#n)")] long taskNumber,
        string? title = null, string? description = null, string? kind = null,
        [Description("claude | user")] string? executor = null,
        [Description("Parametros (substitui todos): objeto ou JSON em texto")] JsonElement? @params = null,
        [Description("Executar o item logo depois (a pessoa pediu para seguir)")] bool execute = false,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = RequireCard(card);
        var owner = (await items.ListAsync(c, ct)).FirstOrDefault(i => i.Tasks.Any(t => t.Number == taskNumber))
                    ?? throw new McpException($"A sub-tarefa #{taskNumber} nao e de um item do card {c}.");
        var input = new WorkTaskInput
        {
            Title = title, Description = description, Kind = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim(), Executor = executor,
            Params = AsObject(@params) is { } po ? ToStringMap(po) : null
        };
        if (description is not null
            && await citations.CheckWorkAsync(c, [description], [(title ?? string.Empty, input.Kind ?? string.Empty, description)], ct) is { } incomplete)
            throw new McpException(incomplete);
        return await specialist.RunAsync(c, $"Editar a sub-tarefa #{taskNumber} do item #{owner.Number}", title ?? description, async () =>
        {
            var actor = await ActorAsync(ct);
            var item = await items.UpdateTaskAsync(taskNumber, input, actor, ct);
            if (execute)
                item = await items.ExecuteAsync(item.Number, actor, ct);
            var task = item.Tasks.First(t => t.Number == taskNumber);
            return (Serialize(new { item = new { item.Number, item.Status }, task = new { task.Number, task.Order, task.Kind, task.Title, task.Status, task.Executor } }),
                $"sub-tarefa #{taskNumber} editada{(execute ? " e item na fila" : string.Empty)}");
        }, ct);
    });

    [McpServerTool(Name = "prmake_work_task_move")]
    [Description("Muda a ORDEM de uma sub-tarefa ainda nao feita — de item avulso OU de item que espelha o plano (a etapa muda de lugar " +
                 "no plano e o item acompanha). position: first (antes da primeira ainda nao feita), last, up, down ou before (+ before = #n). " +
                 "Nunca deixa o que depende de outra passar na frente dela (o erro diz qual depende de qual). Se o Claude estava fazendo a " +
                 "sub-tarefa atropelada, a execucao para e recomeca na ordem nova. NUNCA cancele o item e recrie so para trocar a ordem ou " +
                 "por uma sub-tarefa na frente: acrescente (prmake_work_item_add_task position=first) e/ou mova. Especialista do caso: so com " +
                 "pedido da pessoa na conversa.")]
    public Task<string> MoveTask(string card, [Description("Numero da sub-tarefa a mover (#n)")] long taskNumber,
        [Description("Numero da sub-tarefa (#n) antes da qual ela passa a rodar (position before)")] long? before = null,
        [Description("Executar o item logo depois (a pessoa pediu para seguir)")] bool execute = false,
        [Description("first | last | up | down | before (padrao: before com 'before', senao last)")] string? position = null,
        CancellationToken ct = default) =>
        Control(card, taskNumber, WorkTaskControl.Move, position, before, null, null, execute ? true : null, ct);

    [McpServerTool(Name = "prmake_work_task_control")]
    [Description("Mexe numa sub-tarefa SEM cancelar o item (avulso ou espelho do plano): action move (position first|last|up|down|before " +
                 "+ before #n), pause (o Claude nao a faz — nem as que dependem dela — ate alguem continuar), resume (continua; retoma o " +
                 "item se ja tinha comecado) ou cancel (so ela; se outras dependem dela, informe dependents: cancel = cancela junto, " +
                 "keep = seguem sem ela). Se o Claude estava nela, a execucao para e recomeca na ordem nova. As regras do que pode e " +
                 "por que nao (dependencias) aparecem em prmake_work_item → tasks[].controls. Especialista do caso: so com pedido da " +
                 "pessoa na conversa.")]
    public Task<string> ControlTask(string card, [Description("Numero da sub-tarefa (#n)")] long taskNumber,
        [Description("move | pause | resume | cancel")] string action,
        [Description("move: first | last | up | down | before")] string? position = null,
        [Description("move before: numero (#n) da sub-tarefa ainda nao feita antes da qual ela passa")] long? before = null,
        [Description("Motivo (vai para o item e a Timeline)")] string? reason = null,
        [Description("cancel com dependentes: cancel (cancela junto) | keep (seguem sem ela)")] string? dependents = null,
        [Description("Retomar a execucao depois (padrao: so se parou a execucao ou ao continuar item ja comecado)")] bool? execute = null,
        CancellationToken ct = default) =>
        Control(card, taskNumber, action, position, before, reason, dependents, execute, ct);

    private Task<string> Control(string card, long taskNumber, string action, string? position, long? before, string? reason,
        string? dependents, bool? execute, CancellationToken ct) => Safe(async () =>
    {
        var c = RequireCard(card);
        var owner = (await items.ListAsync(c, ct)).FirstOrDefault(i => i.Tasks.Any(t => t.Number == taskNumber))
                    ?? throw new McpException($"A sub-tarefa #{taskNumber} nao e de um item do card {c}.");
        if (before is { } b && owner.Tasks.All(t => t.Number != b))
            throw new McpException($"A sub-tarefa #{b} nao e do item #{owner.Number}.");
        var verb = (action ?? string.Empty).Trim().ToLowerInvariant();
        var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (specialist.Via is { } via) why = why is null ? via : $"{why} ({via})";
        var what = verb switch
        {
            WorkTaskControl.Move => $"#{taskNumber} {TaskPosition.Label(TaskPosition.Normalize(position, before is not null), before is { } x ? $"#{x}" : null)}",
            WorkTaskControl.Pause => $"pausar a #{taskNumber}",
            WorkTaskControl.Resume => $"continuar a #{taskNumber}",
            WorkTaskControl.Cancel => $"cancelar a #{taskNumber}{(dependents?.Trim().ToLowerInvariant() == WorkTaskControl.DependentsCancel ? " e as que dependem dela" : string.Empty)}",
            _ => throw new McpException("action: move | pause | resume | cancel."),
        };
        var title = verb == WorkTaskControl.Move ? $"Mudar a ordem no item #{owner.Number}" : $"Sub-tarefa do item #{owner.Number}";
        return await specialist.RunAsync(c, title, why is null ? what : $"{what} — {why}", async () =>
        {
            var item = await items.ControlTaskAsync(taskNumber, new WorkTaskControlRequest
            {
                Action = verb, Position = position, Before = before, Reason = why, Dependents = dependents, Execute = execute,
            }, await ActorAsync(ct), ct);
            return (Serialize(new
            {
                item.Number, item.Status,
                tasks = item.Tasks.OrderBy(t => t.Order).Select(t => new
                {
                    t.Number, t.Order, t.Kind, t.Title, t.Status, held = t.Held ? true : (bool?)null,
                    dependsOn = t.Controls is { DependsOn.Count: > 0 } d ? d.DependsOn : null,
                })
            }), $"{what}{(item.Status is WorkItemStatus.Queued or WorkItemStatus.Running ? " — execucao segue na ordem nova" : string.Empty)}");
        }, ct);
    });

    /// <summary>Chave da etapa a partir do título (sem acento, minúsculas, '-'), única no plano.</summary>
    public static string NewStepKey(string title, IEnumerable<string> existing)
    {
        var plain = new string((title ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());
        var slug = string.Join('-', plain.Split('-', StringSplitOptions.RemoveEmptyEntries).Take(6));
        var key = $"{ExecutionStep.RequestedKeyPrefix}{(slug.Length == 0 ? "etapa" : slug)}";
        if (key.Length > 60) key = key[..60].TrimEnd('-');
        var taken = existing.ToHashSet(StringComparer.Ordinal);
        var candidate = key;
        for (var n = 2; taken.Contains(candidate); n++)
            candidate = $"{key}-{n}";
        return candidate;
    }

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
        var owner = (await items.ListAsync(c, ct)).FirstOrDefault(i => i.Tasks.Any(t => t.Number == taskNumber))
                    ?? throw new McpException($"A sub-tarefa #{taskNumber} nao e de um item do card {c}.");
        var verb = (action ?? string.Empty).Trim().ToLowerInvariant();
        // "Ja resolvi" e acao da pessoa (repassada pelo especialista), nao mudanca do plano.
        if (verb != "resolve" && specialist.IsSpecialist && owner.PlanId is not null && owner.Tasks.First(t => t.Number == taskNumber).StepKey is not null)
            throw new McpException($"A sub-tarefa #{taskNumber} espelha a etapa '{owner.Tasks.First(t => t.Number == taskNumber).StepKey}' do plano " +
                                   "— mude pela etapa (prmake_advance conclui; prmake_step muda status/motivo/titulo/descricao); para mover, pausar, " +
                                   "continuar ou cancelar so ela: prmake_work_task_control.");
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
                // Card 78680: a proxima pela ordem ATUAL (a pessoa pode ter reordenado), pulando as pausadas e as presas a uma pausada.
                next = item.Tasks.OrderBy(t => t.Order).FirstOrDefault(t => t.Status is WorkTaskStatus.Pending or WorkTaskStatus.Waiting
                                                                           && !t.Held && t.Controls?.WaitsForHeld is null) is { } n
                    ? new { n.Number, n.Title, n.Kind, n.Status }
                    : null,
                held = item.Tasks.Where(t => t.Held).Select(t => t.Number).ToList() is { Count: > 0 } h ? h : null
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
        return p.ValueKind == JsonValueKind.Object ? ToStringMap(p) : map;
    }

    /// <summary>Objeto JSON → parâmetros em texto (listas viram "a,b").</summary>
    private static Dictionary<string, string> ToStringMap(JsonElement p)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
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
