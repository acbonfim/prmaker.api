using System.Text.Json;
using System.Text.Json.Nodes;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.github.application.Contract;
using solvace.prform.domain.Promotion;

namespace solvace.prform.Skills;

/// <summary>Corpo do <c>POST WorkItems/card/{card}/from-template</c> (e do <c>prmake_tool_run</c>).</summary>
public class CreateFromTemplateRequest
{
    public string? TemplateKey { get; set; }
    /// <summary>Respostas do <c>ask</c> do modelo (<c>{key: valor}</c>; lista vira texto separado por vírgula).</summary>
    public Dictionary<string, JsonElement>? Answers { get; set; }
    public string? Title { get; set; }
    public string? Type { get; set; }
    public bool AllowDuplicate { get; set; }
    public string? DuplicateReason { get; set; }
}

/// <summary>
/// 0085 r4: catálogo das ferramentas (modelos de itens de trabalho) para o card e criação de item a partir de um modelo —
/// a mesma lógica no REST (<c>GET WorkItems/catalog</c>, <c>POST WorkItems/card/{card}/from-template</c>) e no MCP
/// (<c>prmake_tools</c>, <c>prmake_tool_run</c>). Também é a fonte dos tipos de item (<c>WorkItemTypes</c>) para a criação.
/// </summary>
public class WorkItemToolsService(
    SkillsRulesService rules,
    IPullRequestGithubApplication pullRequests,
    IExecutionPlanApplication plans,
    ILogger<WorkItemToolsService> logger) : IWorkItemTypeSource
{
    /// <summary>Tipos de item semeados (o mesmo da migração <c>SeedSpecialistTools0085c</c>) — reserva sem <c>WorkItemTypes</c>.</summary>
    public const string DefaultItemTypesJson = solvace.prform.infra.Migrations.SeedSpecialistTools0085c.ItemTypes;

    // ── Tipos de item (IWorkItemTypeSource) ─────────────────────────────────────────────────────

    public async Task<WorkItemTypeSettings> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await rules.SettingsOnlyAsync(cancellationToken);
        var keys = ItemTypes(settings).OfType<JsonObject>()
            .Select(t => WorkItemTemplateRenderer.Str(t, "key")?.Trim().ToLowerInvariant())
            .Where(k => !string.IsNullOrEmpty(k)).Select(k => k!).Distinct().ToList();
        var templateTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Templates(settings).OfType<JsonObject>())
            if (WorkItemTemplateRenderer.Str(t, "key") is { Length: > 0 } key && WorkItemTemplateRenderer.Str(t, "type") is { Length: > 0 } type)
                templateTypes[key.Trim()] = type.Trim();
        return new WorkItemTypeSettings(keys, templateTypes, Value(settings, SkillsConfigurationKeys.TicketSystem));
    }

    /// <summary><c>WorkItemTypes</c> configurado (lista de objetos) ou o padrão semeado.</summary>
    public static JsonArray ItemTypes(IReadOnlyDictionary<string, string> settings)
    {
        var configured = ParseArray(Value(settings, SkillsConfigurationKeys.WorkItemTypes));
        if (configured.OfType<JsonObject>().Any()) return configured;
        var defaults = ParseArray(DefaultItemTypesJson);
        defaults.Add(JsonNode.Parse(solvace.prform.infra.Migrations.SeedEnvValidation0092.NewItemType));
        return defaults;
    }

    private static JsonArray Templates(IReadOnlyDictionary<string, string> settings) =>
        ParseArray(Value(settings, SkillsConfigurationKeys.WorkItemTemplates));

    // ── Contexto ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Contexto do renderizador para o card (regras + repositórios do card).</summary>
    public async Task<WorkItemTemplateContext> ContextAsync(CardRules r, CancellationToken ct)
    {
        var repos = await CardRepositoriesAsync(r.Card, ct);
        string V(string key) => Value(r.Settings, key);
        return new WorkItemTemplateContext
        {
            Card = r.Card,
            Type = r.WorkItemType is null ? null : r.IsUserStory ? "user-story" : "bug",
            Flow = r.Flow,
            State = r.State,
            MainBranch = r.MainBranch,
            Suffixes = r.Suffixes,
            Repository = repos.FirstOrDefault() ?? r.DefaultRepository,
            CardRepositories = repos,
            BranchStrategyJson = V(SkillsConfigurationKeys.BranchStrategy),
            Profiles = PromotionProfileSet.Parse(V(SkillsConfigurationKeys.PromotionProfiles)),
            QaBranchPattern = V(SkillsConfigurationKeys.QaBranchPattern),
            TicketSystem = V(SkillsConfigurationKeys.TicketSystem),
            Environments = ValidationEnvironments.Parse(V(SkillsConfigurationKeys.ValidationEnvironments)),
            EnvironmentDefaults = ValidationEnvironments.Defaults(V(SkillsConfigurationKeys.ValidationEnvironments))
        };
    }

    /// <summary>
    /// Repositórios do card, o do PR principal primeiro: PRs registrados (o mais antigo primeiro); sem PR, as etapas de PR
    /// (<c>pr-&lt;repo&gt;</c>) e de código com repositório do plano de correção mais recente.
    /// </summary>
    public async Task<IReadOnlyList<string>> CardRepositoriesAsync(string card, CancellationToken ct)
    {
        var result = new List<string>();
        void Add(string? repo)
        {
            var name = repo?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            var slash = name.LastIndexOf('/');
            if (slash >= 0) name = name[(slash + 1)..];
            if (name.Length > 0 && !result.Contains(name, StringComparer.OrdinalIgnoreCase)) result.Add(name);
        }

        try
        {
            var prs = await pullRequests.ListByCard(card, refreshStatus: false, ct);
            foreach (var pr in prs.OrderBy(p => p.CreatedAt))
                Add(pr.RepositoryId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "PRs do card {Card} indisponíveis para o catálogo", card);
        }
        if (result.Count > 0) return result;

        try
        {
            // Plano de correção mais recente; sem ele, o de análise (o repositório que a análise apontou) — assim a primeira
            // sugestão de branch já segue o repositório em tratamento, mesmo antes de existir PR ou correção.
            var cardPlans = (await plans.GetByCardAsync(card, ct)).Where(p => p.Status != ExecutionStatus.Cancelled).ToList();
            var latest = cardPlans.Where(p => p.Phase == ExecutionPhase.Correction).OrderByDescending(p => p.CreatedAt).FirstOrDefault()
                         ?? cardPlans.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
            if (latest is not null)
            {
                var plan = await plans.GetAsync(latest.Id, ct);
                var steps = plan.Steps.OrderBy(s => s.Order).ToList();
                foreach (var s in steps.Where(s => s.Kind == ExecutionStepKind.PullRequest || s.Key.StartsWith("pr-", StringComparison.OrdinalIgnoreCase)))
                    Add(s.Repository is { Length: > 0 } repo ? repo : s.Key.StartsWith("pr-", StringComparison.OrdinalIgnoreCase) ? s.Key[3..] : null);
                foreach (var s in steps)
                    Add(s.Repository);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Plano do card {Card} indisponível para o catálogo", card);
        }
        return result;
    }

    /// <summary>Branches/destinos do fluxo para cada repositório do card (ou o padrão, sem PR/plano) — só os destinos que o tipo dele tem.</summary>
    public async Task<IReadOnlyList<RepositoryBranchPlan>> RepositoryPlansAsync(CardRules r, CancellationToken ct)
    {
        var repos = (await CardRepositoriesAsync(r.Card, ct)).ToList();
        if (repos.Count == 0 && r.DefaultRepository is { Length: > 0 } fallback) repos.Add(fallback);
        return repos.Select(repo => SkillsRulesService.PlanForRepository(r, repo)).ToList();
    }

    // ── Catálogo ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Catálogo para o card: tipos de sub-tarefa (com <c>input</c>), tipos de item (<c>itemTypes</c>) e modelos com
    /// <c>applicable</c>/<c>reason</c> (pelo <c>when</c>), <c>ask</c> resolvido (<c>options</c>, <c>default</c>) e textos
    /// renderizados. Sem card: os modelos como estão (todos aplicáveis).
    /// </summary>
    public async Task<JsonObject> CatalogAsync(string? card, CancellationToken ct)
    {
        var c = SkillsRulesService.NormalizeCard(card);
        CardRules? r = c.Length > 0 ? await rules.GetAsync(c, ct) : null;
        var settings = r?.Settings ?? await rules.SettingsOnlyAsync(ct);

        var kinds = ParseArray(Value(settings, SkillsConfigurationKeys.WorkItemTaskKinds));
        WorkItemParamInputs.Apply(kinds, Value(settings, SkillsConfigurationKeys.TicketSystem),
            ValidationEnvironments.Parse(Value(settings, SkillsConfigurationKeys.ValidationEnvironments)));
        var templates = Templates(settings);
        var ctx = r is null ? null : await ContextAsync(r, ct);

        foreach (var t in templates.OfType<JsonObject>())
        {
            if (ctx is null)
            {
                t["applicable"] = true;
                continue;
            }
            var (applicable, reason) = WorkItemTemplateRenderer.Evaluate(t, ctx);
            var asks = WorkItemTemplateRenderer.ResolveAsks(t, ctx);
            WorkItemTemplateRenderer.RenderForCatalog(t, ctx);
            if (t.ContainsKey("ask")) t["ask"] = asks;
            t["applicable"] = applicable;
            if (reason is not null) t["reason"] = reason;
        }

        var result = new JsonObject
        {
            ["card"] = r?.Card,
            ["type"] = r is null ? null : r.IsUserStory ? "user-story" : "bug",
            ["mainBranch"] = r?.MainBranch,
            ["branchesByTarget"] = r is null ? null : new JsonObject(r.Suffixes.Select(s =>
                new KeyValuePair<string, JsonNode?>(s.Key, BranchRules.BranchFor(r.MainBranch, s.Key, r.Suffixes)))),
            ["flow"] = r?.Flow,
            ["state"] = r?.State,
            ["repository"] = ctx?.Repository,
            ["repositories"] = ctx is null ? null : new JsonArray(ctx.CardRepositories.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["repoKind"] = ctx is null ? null : WorkItemTemplateRenderer.KindOf(ctx, ctx.Repository),
            ["taskKinds"] = kinds,
            ["itemTypes"] = ItemTypes(settings),
            ["templates"] = templates
        };
        return result;
    }

    // ── from-template ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renderiza o modelo com as respostas e monta o pedido de criação (tipo: o pedido, senão o do modelo; sub-tarefa sem
    /// executor herda o do tipo de sub-tarefa). Erros: <see cref="WorkItemTemplateException"/>.
    /// </summary>
    public async Task<CreateWorkItemRequest> BuildCreateRequestAsync(string card, CreateFromTemplateRequest request, CancellationToken ct)
    {
        var key = request.TemplateKey?.Trim();
        if (string.IsNullOrEmpty(key))
            throw new WorkItemTemplateException(WorkItemTemplateException.NotFound, "Informe o modelo (templateKey).");
        var r = await rules.GetAsync(card, ct);
        var template = Templates(r.Settings).OfType<JsonObject>()
                           .FirstOrDefault(t => string.Equals(WorkItemTemplateRenderer.Str(t, "key")?.Trim(), key, StringComparison.OrdinalIgnoreCase))
                       ?? throw new WorkItemTemplateException(WorkItemTemplateException.NotFound, $"Modelo '{key}' não existe no catálogo (WorkItemTemplates).");
        var ctx = await ContextAsync(r, ct);
        var executors = ParseArray(Value(r.Settings, SkillsConfigurationKeys.WorkItemTaskKinds)).OfType<JsonObject>()
            .Where(k => WorkItemTemplateRenderer.Str(k, "kind") is { Length: > 0 })
            .GroupBy(k => WorkItemTemplateRenderer.Str(k, "kind")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => WorkItemTemplateRenderer.Str(g.First(), "executor") ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var rendered = WorkItemTemplateRenderer.Render(template, ctx, AnswersOf(request.Answers), executors);
        return new CreateWorkItemRequest
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? rendered.Title : request.Title.Trim(),
            Description = rendered.Description,
            Type = string.IsNullOrWhiteSpace(request.Type) ? rendered.Type : request.Type.Trim(),
            TemplateKey = rendered.Key,
            AllowDuplicate = request.AllowDuplicate,
            DuplicateReason = request.DuplicateReason,
            Tasks = rendered.Tasks.Select(t => new WorkTaskInput
            {
                Title = t.Title, Description = t.Description, Kind = t.Kind, Executor = t.Executor, Params = t.Params
            }).ToList()
        };
    }

    /// <summary>Respostas como texto: string como está, lista → itens separados por vírgula, null → vazio.</summary>
    public static Dictionary<string, string> AnswersOf(IReadOnlyDictionary<string, JsonElement>? answers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (answers is null) return map;
        foreach (var (k, v) in answers)
            map[k] = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? string.Empty,
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                JsonValueKind.Array => string.Join(",", v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText())),
                _ => v.GetRawText()
            };
        return map;
    }

    // ── Util ─────────────────────────────────────────────────────────────────────────────────────

    private static string Value(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var v) ? v : string.Empty;

    public static JsonArray ParseArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonNode.Parse(json) as JsonArray ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
