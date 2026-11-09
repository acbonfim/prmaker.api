using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using solvace.prform.domain.Promotion;

namespace solvace.prform.Skills;

/// <summary>
/// 0085 r4: o que o renderizador dos modelos sabe do card — tipo, fluxo, estado, branch principal, sufixos por destino,
/// repositório do PR principal (PRs registrados / etapas <c>pr-&lt;repo&gt;</c> do plano; reserva <c>DefaultRepository</c>),
/// <c>BranchStrategy</c>, <c>PromotionProfiles</c>, <c>QaBranchPattern</c> e <c>TicketSystem</c>.
/// </summary>
public sealed record WorkItemTemplateContext
{
    public string Card { get; init; } = string.Empty;
    /// <summary><c>bug</c> | <c>user-story</c>; null = tipo não lido (o <c>when.type</c> não filtra).</summary>
    public string? Type { get; init; }
    public string? Flow { get; init; }
    public string? State { get; init; }
    public string MainBranch { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Suffixes { get; init; } = BranchRules.ParseSuffixes(null);
    /// <summary>Repositório do PR principal do card (ou o padrão).</summary>
    public string? Repository { get; init; }
    /// <summary>Repositórios do card (PRs registrados e etapas de PR do plano), o principal primeiro.</summary>
    public IReadOnlyList<string> CardRepositories { get; init; } = [];
    public string? BranchStrategyJson { get; init; }
    public PromotionProfileSet Profiles { get; init; } = PromotionProfileSet.Empty;
    public string? QaBranchPattern { get; init; }
    public string? TicketSystem { get; init; }
    /// <summary>0092: ambientes de validação (<c>ValidationEnvironments</c>) — opções do ask e rótulo do <c>{eachLabel}</c>.</summary>
    public IReadOnlyList<ValidationEnvironment> Environments { get; init; } = [];
    /// <summary>0092: ambientes sugeridos por tipo do card (<c>ValidationEnvironments.defaults</c>) — o <c>{validationDefaults}</c>.</summary>
    public IReadOnlyDictionary<string, string> EnvironmentDefaults { get; init; } = new Dictionary<string, string>();
}

/// <summary>Modelo renderizado, pronto para virar <c>CreateWorkItemRequest</c>.</summary>
public sealed record RenderedWorkItem(string Key, string Title, string? Description, string? Type, IReadOnlyList<RenderedWorkTask> Tasks,
    IReadOnlyDictionary<string, string> Answers);

public sealed record RenderedWorkTask(string Title, string? Description, string Kind, string? Executor, Dictionary<string, string> Params);

/// <summary>Erro de quem pediu (modelo inexistente, resposta obrigatória faltando, destino inválido, não se aplica) → 400/404.</summary>
public class WorkItemTemplateException(string code, string message, IReadOnlyList<string>? missing = null) : Exception(message)
{
    public const string NotFound = "templateNotFound";
    public const string InvalidAnswers = "invalidAnswers";
    public const string NotApplicable = "notApplicable";

    public string Code { get; } = code;
    public IReadOnlyList<string> Missing { get; } = missing ?? [];
}

/// <summary>
/// 0085 r4: renderiza os modelos de itens de trabalho (<c>WorkItemTemplates</c>) — usado pelo catálogo
/// (<c>GET WorkItems/catalog</c>, <c>prmake_tools</c>) e pela criação a partir do modelo (<c>POST WorkItems/card/{card}/from-template</c>,
/// <c>prmake_tool_run</c>). Formato opcional dos modelos: <c>when {type[], flow[], repoKinds[], states[]}</c> e
/// <c>ask [{key, label, input, options?, default?, required?}]</c>. Marcadores: <c>{card}</c>, <c>{branch}</c>,
/// <c>{suffix:&lt;destino&gt;}</c>, <c>{suffix:@&lt;ask&gt;}</c> (sufixo do destino respondido), <c>{ask:&lt;key&gt;}</c>,
/// <c>{repository}</c> (o respondido em <c>ask.repository</c>, senão o do PR principal do card), <c>{repoKind}</c>,
/// <c>{flow}</c>, <c>{flowTargets}</c>, <c>{base}</c>, <c>{qaBranch}</c>, <c>{promotionTargets}</c> e <c>{ticketSystem}</c>.
/// 0092: <c>{validationEnvironments}</c> nas <c>options</c> do ask (os ambientes de validação como <c>{value, label}</c>),
/// <c>{validationDefaults}</c> (ambientes sugeridos para o tipo do card) e sub-tarefa com <c>repeat</c> — uma por valor
/// (separados por vírgula) do texto renderizado, com <c>{each}</c> (o valor) e <c>{eachLabel}</c> (o rótulo da opção).
/// Marcador desconhecido fica como está.
/// </summary>
public static partial class WorkItemTemplateRenderer
{
    public const string PromotionTargetsMarker = "{promotionTargets}";

    /// <summary>Nomes de marcador que o renderizador resolve (sem parâmetro).</summary>
    public static readonly IReadOnlySet<string> SimpleMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "card", "branch", "repository", "repoKind", "flow", "flowTargets", "base", "qaBranch", "promotionTargets", "ticketSystem",
          "each", "eachLabel", "validationEnvironments", "validationDefaults" };

    // ── Contexto derivado ────────────────────────────────────────────────────────────────────────

    /// <summary>Tipo do repositório pelo <c>BranchStrategy.repositories</c>.</summary>
    public static string? KindOf(WorkItemTemplateContext ctx, string? repository) =>
        BranchStrategyKinds.KindOf(repository, ctx.BranchStrategyJson);

    /// <summary>Base do fluxo do card para o tipo de repositório: <c>base</c>, senão a primeira de <c>baseOptions</c>, senão master.</summary>
    public static string BaseFor(WorkItemTemplateContext ctx, string? kind)
    {
        var k = FlowKind(ctx, kind);
        return k?.Base is { Length: > 0 } b ? b : k?.BaseOptions.FirstOrDefault() ?? "master";
    }

    /// <summary>Destinos dos PRs do fluxo do card para o tipo (<c>development, qa</c>; release: <c>release-version/hotfix-version</c>).</summary>
    public static string FlowTargetsFor(WorkItemTemplateContext ctx, string? kind)
    {
        var k = FlowKind(ctx, kind);
        if (k is null) return string.Empty;
        return string.Join(", ", k.Prs
            .Select(p => p.Target.Contains("{base}", StringComparison.OrdinalIgnoreCase) && k.BaseOptions.Count > 0
                ? string.Join("/", k.BaseOptions)
                : p.Target)
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static CardFlowKind? FlowKind(WorkItemTemplateContext ctx, string? kind)
    {
        if (ctx.Flow is not { Length: > 0 } flow) return null;
        var kinds = SkillsRulesService.KindsOf(ctx.BranchStrategyJson, flow, ctx.MainBranch);
        return kinds.FirstOrDefault(k => string.Equals(k.Kind, kind, StringComparison.OrdinalIgnoreCase))
               ?? (kind is null ? kinds.FirstOrDefault() : null);
    }

    /// <summary>Branch de QA: a primeira entrada do <c>QaBranchPattern</c> (sem o <c>*</c> de prefixo); padrão <c>qa</c>.</summary>
    public static string QaBranch(WorkItemTemplateContext ctx)
    {
        var first = (ctx.QaBranchPattern ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.TrimEnd('*').Trim())
            .FirstOrDefault(p => p.Length > 0);
        return first ?? "qa";
    }

    /// <summary>Primeiro sistema de chamados do <c>TicketSystem</c> (lista separada por vírgula).</summary>
    public static string TicketSystem(WorkItemTemplateContext ctx) =>
        (ctx.TicketSystem ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// Destinos de promoção do tipo de repositório (<c>PromotionProfiles</c>), na ordem do perfil. Tipo desconhecido → os de
    /// todos os perfis. Modelo com marcador (<c>release-{version}</c>) fica de fora.
    /// </summary>
    public static IReadOnlyList<string> PromotionTargets(WorkItemTemplateContext ctx, string? kind)
    {
        var profiles = kind is { Length: > 0 } && ctx.Profiles.Find(kind) is { } p ? [p] : kind is { Length: > 0 } ? [] : ctx.Profiles.All;
        return profiles.SelectMany(x => x.Targets).Select(t => t.Branch.Trim())
            .Where(b => b.Length > 0 && !b.Contains('{'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Destinos conhecidos para o tipo: perfil de promoção + bases, opções de base e destinos dos PRs de todos os fluxos.</summary>
    public static IReadOnlySet<string> KnownTargetsFor(WorkItemTemplateContext ctx, string kind)
    {
        var set = new HashSet<string>(PromotionTargets(ctx, kind), StringComparer.OrdinalIgnoreCase);
        foreach (var flow in FlowNames(ctx.BranchStrategyJson))
        foreach (var k in SkillsRulesService.KindsOf(ctx.BranchStrategyJson, flow, ctx.MainBranch)
                     .Where(k => string.Equals(k.Kind, kind, StringComparison.OrdinalIgnoreCase)))
        {
            if (k.Base is { Length: > 0 } b) set.Add(b);
            foreach (var o in k.BaseOptions) set.Add(o);
            foreach (var pr in k.Prs.Where(p => !p.Target.Contains('{'))) set.Add(pr.Target);
        }
        return set;
    }

    /// <summary>
    /// Destino conhecido de OUTRO tipo de repositório e que não vale para este (ex.: <c>hotfix-version</c> em revamp-frontend):
    /// motivo do bloqueio, senão null. Branch que ninguém conhece (outra) passa.
    /// </summary>
    public static string? InvalidTargetReason(WorkItemTemplateContext ctx, string? kind, string? target)
    {
        var t = target?.Trim();
        if (string.IsNullOrEmpty(t) || kind is not { Length: > 0 }) return null;
        var own = KnownTargetsFor(ctx, kind);
        if (own.Count == 0 || own.Contains(t)) return null;
        var others = new HashSet<string>(ctx.Suffixes.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var p in ctx.Profiles.All)
            foreach (var x in p.Targets) others.Add(x.Branch.Trim());
        return others.Contains(t)
            ? $"O destino {t} não vale para repositórios {kind} (destinos conhecidos: {string.Join(", ", own)})."
            : null;
    }

    private static IEnumerable<string> FlowNames(string? strategyJson)
    {
        if (string.IsNullOrWhiteSpace(strategyJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(strategyJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("flows", out var flows)
                                                                    && flows.ValueKind == JsonValueKind.Object
                ? flows.EnumerateObject().Select(f => f.Name).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ── when ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// O modelo vale para o card? Confere <c>when.type</c>, <c>when.flow</c>, <c>when.repoKinds</c> (do repositório informado ou
    /// do PR principal), <c>when.flowTargets</c> (destinos que o fluxo do repositório tem) e <c>when.states</c>. O que não se sabe do card (tipo/fluxo/estado/tipo do repo não lidos) não bloqueia.
    /// </summary>
    public static (bool Applicable, string? Reason) Evaluate(JsonObject template, WorkItemTemplateContext ctx, string? repository = null)
    {
        if (template["when"] is not JsonObject when) return (true, null);
        var repo = Clean(repository) ?? ctx.Repository;

        var types = Strings(when["type"]);
        if (types.Count > 0 && ctx.Type is { Length: > 0 } type && !types.Contains(type, StringComparer.OrdinalIgnoreCase))
            return (false, $"Só para cards do tipo {string.Join(" ou ", types.Select(TypeLabel))} (este card é {TypeLabel(type)}).");

        var flows = Strings(when["flow"]);
        if (flows.Count > 0 && ctx.Flow is { Length: > 0 } flow && !flows.Contains(flow, StringComparer.OrdinalIgnoreCase))
            return (false, $"Só no fluxo {string.Join(" ou ", flows)} (este card está no fluxo {flow}).");

        var repoKinds = Strings(when["repoKinds"]);
        if (repoKinds.Count > 0 && KindOf(ctx, repo) is { } kind && !repoKinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
            return (false, $"Só para repositórios {string.Join(", ", repoKinds)} ({repo} é {kind}).");

        // when.flowTargets: o fluxo do card, para o tipo do repositório, precisa ter PR para algum destes destinos (ex.: ["qa"]
        // — revamp-backend não tem QA). Tipo/fluxo não lidos ou sem regra para o tipo não bloqueiam.
        var flowTargets = Strings(when["flowTargets"]);
        if (flowTargets.Count > 0 && KindOf(ctx, repo) is { } flowKindName && FlowKind(ctx, flowKindName) is { } flowKind)
        {
            var have = FlowPrTargets(flowKind);
            if (have.Count > 0 && !flowTargets.Any(t => have.Contains(t, StringComparer.OrdinalIgnoreCase)))
                return (false, $"O fluxo de {repo} ({flowKindName}) não tem destino {string.Join(" ou ", flowTargets)} (destinos do fluxo: {string.Join(", ", have)}).");
        }

        var states = Strings(when["states"]);
        if (states.Count > 0 && ctx.State is { Length: > 0 } state && !states.Contains(state, StringComparer.OrdinalIgnoreCase))
            return (false, $"Só com o card em {string.Join(", ", states)} (está em {state}).");

        return (true, null);
    }

    /// <summary>Destinos dos PRs do fluxo para o tipo (<c>{base}</c> vira as opções de base), sem repetir.</summary>
    private static List<string> FlowPrTargets(CardFlowKind kind) => kind.Prs
        .SelectMany(p => p.Target.Contains("{base}", StringComparison.OrdinalIgnoreCase)
            ? kind.BaseOptions.Count > 0 ? kind.BaseOptions : kind.Base is { Length: > 0 } b ? [b] : []
            : (IEnumerable<string>)[p.Target])
        .Where(t => t.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string TypeLabel(string type) =>
        string.Equals(type, "user-story", StringComparison.OrdinalIgnoreCase) ? "User Story"
        : string.Equals(type, "bug", StringComparison.OrdinalIgnoreCase) ? "Bug" : type;

    // ── ask ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Perguntas do modelo para o catálogo: <c>options</c> resolvidas (<c>"{promotionTargets}"</c> → destinos do perfil do
    /// repositório; lista/texto com marcadores renderizados), <c>default</c> renderizado e, para destinos de promoção,
    /// <c>optionsByRepository</c> (destinos por repositório do card, para quando a pessoa troca o repositório).
    /// </summary>
    public static JsonArray ResolveAsks(JsonObject template, WorkItemTemplateContext ctx)
    {
        var result = new JsonArray();
        if (template["ask"] is not JsonArray asks) return result;
        var defaults = DefaultAnswers(template, ctx);
        var repo = defaults.TryGetValue("repository", out var r) && r.Length > 0 ? r : ctx.Repository;
        foreach (var ask in asks.OfType<JsonObject>())
        {
            var copy = (JsonObject)ask.DeepClone();
            var key = Str(ask, "key");
            if (key is null) continue;
            var options = ResolveOptions(ask["options"], ctx, repo);
            if (IsEnvironmentsAsk(ask))
                copy["options"] = ValidationEnvironments.Options(ctx.Environments);
            else if (options is not null)
                copy["options"] = new JsonArray(options.Select(o => (JsonNode?)JsonValue.Create(o)).ToArray());
            if (IsPromotionMarker(ask["options"]))
            {
                var byRepo = new JsonObject();
                foreach (var cardRepo in ctx.CardRepositories.Append(ctx.Repository).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                    byRepo[cardRepo] = new JsonArray(PromotionTargets(ctx, KindOf(ctx, cardRepo)).Select(o => (JsonNode?)JsonValue.Create(o)).ToArray());
                copy["optionsByRepository"] = byRepo;
            }
            if (defaults.TryGetValue(key, out var d) && d.Length > 0)
                copy["default"] = d;
            else
                copy.Remove("default");
            if (string.Equals(Str(ask, "input"), WorkItemParamInputs.Repository, StringComparison.OrdinalIgnoreCase) && ctx.CardRepositories.Count > 0)
                copy["suggestions"] = new JsonArray(ctx.CardRepositories.Select(o => (JsonNode?)JsonValue.Create(o)).ToArray());
            result.Add(copy);
        }
        return result;
    }

    /// <summary>0092: pergunta de ambientes de validação (opções <c>{validationEnvironments}</c> ou input <c>environments</c> sem opções).</summary>
    private static bool IsEnvironmentsAsk(JsonObject ask) =>
        IsEnvironmentsMarker(ask["options"])
        || (string.Equals(Str(ask, "input"), ValidationEnvironments.Input, StringComparison.OrdinalIgnoreCase) && ask["options"] is null);

    private static bool IsEnvironmentsMarker(JsonNode? options) =>
        options is JsonValue v && v.TryGetValue<string>(out var s) && string.Equals(s.Trim(), ValidationEnvironments.OptionsMarker, StringComparison.OrdinalIgnoreCase);

    private static bool IsPromotionMarker(JsonNode? options) =>
        options is JsonValue v && v.TryGetValue<string>(out var s) && string.Equals(s.Trim(), PromotionTargetsMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Opções do ask: marcador de destinos → lista do perfil; texto → itens separados por vírgula; lista → cada item renderizado.</summary>
    private static List<string>? ResolveOptions(JsonNode? options, WorkItemTemplateContext ctx, string? repository)
    {
        switch (options)
        {
            case null:
                return null;
            case JsonValue v when v.TryGetValue<string>(out var s):
                if (IsPromotionMarker(v))
                    return PromotionTargets(ctx, KindOf(ctx, repository)).ToList();
                if (IsEnvironmentsMarker(v))
                    return ctx.Environments.Select(e => e.Key).ToList();
                return SplitList(RenderText(s, ctx, Vars(ctx, repository, null)));
            case JsonArray arr:
                return arr.SelectMany(o => o is JsonValue ov && ov.TryGetValue<string>(out var os)
                        ? IsPromotionMarker(ov) ? PromotionTargets(ctx, KindOf(ctx, repository)) : [RenderText(os, ctx, Vars(ctx, repository, null))]
                        : o is null ? [] : [o.ToJsonString()])
                    .Where(o => o.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            default:
                return null;
        }
    }

    private static List<string> SplitList(string text) =>
        text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0).ToList();

    /// <summary>Respostas padrão (o <c>default</c> de cada ask renderizado; <c>repository</c> primeiro, que os outros usam).</summary>
    public static Dictionary<string, string> DefaultAnswers(JsonObject template, WorkItemTemplateContext ctx)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (template["ask"] is not JsonArray asks) return result;
        var ordered = asks.OfType<JsonObject>()
            .OrderBy(a => string.Equals(Str(a, "key"), "repository", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();
        foreach (var ask in ordered)
        {
            if (Str(ask, "key") is not { } key || Str(ask, "default") is not { } d) continue;
            result.TryGetValue("repository", out var repo);
            result[key] = RenderText(d, ctx, Vars(ctx, Clean(repo), result)).Trim();
        }
        return result;
    }

    // ── Render ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renderiza o modelo com as respostas: confere <c>required</c> (resposta vazia usa o <c>default</c>), opções fechadas
    /// (<c>select</c>), destino de outro tipo de repositório e o <c>when</c> (com o repositório respondido). Sub-tarefa sem
    /// executor herda o do tipo (<paramref name="kindExecutors"/>).
    /// </summary>
    public static RenderedWorkItem Render(JsonObject template, WorkItemTemplateContext ctx, IReadOnlyDictionary<string, string>? answers,
        IReadOnlyDictionary<string, string>? kindExecutors = null)
    {
        var key = Str(template, "key") ?? string.Empty;
        var effective = DefaultAnswers(template, ctx);
        foreach (var (k, v) in answers ?? new Dictionary<string, string>())
            if (!string.IsNullOrWhiteSpace(v))
                effective[k.Trim()] = v.Trim();
        var repository = effective.TryGetValue("repository", out var r) ? Clean(r) : null;

        var missing = new List<string>();
        var problems = new List<string>();
        if (template["ask"] is JsonArray asks)
        {
            foreach (var ask in asks.OfType<JsonObject>())
            {
                if (Str(ask, "key") is not { } askKey) continue;
                effective.TryGetValue(askKey, out var value);
                var label = Str(ask, "label") ?? askKey;
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (ask["required"] is JsonValue req && req.TryGetValue<bool>(out var isRequired) && isRequired)
                    {
                        missing.Add(askKey);
                        problems.Add($"Informe \"{label}\" ({askKey}).");
                    }
                    continue;
                }
                var input = Str(ask, "input");
                if (string.Equals(input, WorkItemParamInputs.Select, StringComparison.OrdinalIgnoreCase)
                    && ResolveOptions(ask["options"], ctx, repository) is { Count: > 0 } options
                    && !options.Contains(value, StringComparer.OrdinalIgnoreCase))
                    problems.Add($"\"{label}\": {value} não é uma das opções ({string.Join(", ", options)}).");
                if (string.Equals(input, WorkItemParamInputs.TargetBranch, StringComparison.OrdinalIgnoreCase)
                    && InvalidTargetReason(ctx, KindOf(ctx, repository ?? ctx.Repository), value) is { } invalid)
                    problems.Add(invalid);
            }
        }
        if (problems.Count > 0)
            throw new WorkItemTemplateException(WorkItemTemplateException.InvalidAnswers, string.Join(" ", problems), missing);

        var (applicable, reason) = Evaluate(template, ctx, repository);
        if (!applicable)
            throw new WorkItemTemplateException(WorkItemTemplateException.NotApplicable, $"O modelo {key} não se aplica: {reason}");

        // Pergunta opcional sem resposta some do texto (não fica "{ask:..}" no item).
        var vars = Vars(ctx, repository, effective) with { MissingAsk = _ => string.Empty, MissingSuffix = _ => string.Empty };
        string R(string? text) => RenderText(text, ctx, vars);

        var tasks = new List<RenderedWorkTask>();
        if (template["tasks"] is JsonArray list)
        {
            foreach (var t in list.OfType<JsonObject>())
            {
                if (Str(t, "repeat") is not { } repeat)
                {
                    tasks.Add(RenderTask(t, R));
                    continue;
                }
                // 0092: uma sub-tarefa por valor (ex.: um ambiente de validação por sub-tarefa).
                foreach (var each in SplitList(R(repeat)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var eachVars = vars with { Each = each, EachLabel = EachLabel(template, ctx, each) };
                    tasks.Add(RenderTask(t, text => RenderText(text, ctx, eachVars)));
                }
            }
        }

        RenderedWorkTask RenderTask(JsonObject t, Func<string?, string> render)
        {
            var kind = Str(t, "kind")?.Trim() is { Length: > 0 } k ? k : "manual";
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (t["params"] is JsonObject ps)
                foreach (var (pk, pv) in ps)
                    parameters[pk] = pv switch
                    {
                        JsonValue jv when jv.TryGetValue<string>(out var s) => render(s),
                        null => string.Empty,
                        JsonArray arr => string.Join(",", arr.Select(x => x is JsonValue xv && xv.TryGetValue<string>(out var xs) ? render(xs) : x?.ToJsonString())),
                        _ => pv.ToJsonString()
                    };
            var executor = Str(t, "executor") is { Length: > 0 } e ? e
                : kindExecutors is not null && kindExecutors.TryGetValue(kind, out var ke) && ke.Length > 0 ? ke : null;
            return new RenderedWorkTask(render(Str(t, "title") ?? kind), Str(t, "description") is { } desc ? render(desc) : null, kind, executor, parameters);
        }

        return new RenderedWorkItem(key, R(Str(template, "title") ?? key), Str(template, "description") is { } d2 ? R(d2) : null,
            Str(template, "type"), tasks, effective);
    }

    /// <summary>0092: rótulo do valor do <c>repeat</c> — a opção de algum ask cujo valor bate (ambientes: o nome do ambiente).</summary>
    private static string EachLabel(JsonObject template, WorkItemTemplateContext ctx, string each)
    {
        return template["ask"] is JsonArray asks && asks.OfType<JsonObject>().Any(IsEnvironmentsAsk)
            ? ValidationEnvironments.LabelOf(ctx.Environments, each)
            : each;
    }

    /// <summary>
    /// Catálogo: título/descrição com as respostas padrão (sem resposta: <c>&lt;rótulo&gt;</c>) e sub-tarefas com o resto dos
    /// marcadores resolvidos, mantendo <c>{ask:..}</c>/<c>{suffix:@..}</c> sem resposta padrão para a tela pré-visualizar.
    /// </summary>
    public static void RenderForCatalog(JsonObject template, WorkItemTemplateContext ctx)
    {
        var defaults = DefaultAnswers(template, ctx);
        var repository = defaults.TryGetValue("repository", out var r) ? Clean(r) : null;
        var labels = template["ask"] is JsonArray asks
            ? asks.OfType<JsonObject>().Where(a => Str(a, "key") is not null)
                .GroupBy(a => Str(a, "key")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Str(g.First(), "label") ?? g.Key, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var headVars = Vars(ctx, repository, defaults) with
        {
            MissingAsk = k => $"<{(labels.TryGetValue(k, out var l) ? l : k)}>",
            MissingSuffix = k => $"-<{(labels.TryGetValue(k, out var l) ? l : k)}>"
        };
        foreach (var field in new[] { "title", "description" })
            if (Str(template, field) is { } text)
                template[field] = RenderText(text, ctx, headVars);

        if (template["tasks"] is JsonNode tasksNode)
        {
            var taskVars = Vars(ctx, repository, defaults);
            ExpandStrings(tasksNode, s => RenderText(s, ctx, taskVars));
        }
    }

    /// <summary>Variáveis de uma renderização (repositório efetivo e respostas).</summary>
    public sealed record RenderVars(string Repository, string RepoKind, string FlowTargets, string Base, string PromotionTargets,
        IReadOnlyDictionary<string, string> Answers)
    {
        public Func<string, string?>? MissingAsk { get; init; }
        public Func<string, string?>? MissingSuffix { get; init; }
        /// <summary>0092: valor da sub-tarefa repetida (<c>{each}</c>) e o rótulo (<c>{eachLabel}</c>).</summary>
        public string? Each { get; init; }
        public string? EachLabel { get; init; }
    }

    public static RenderVars Vars(WorkItemTemplateContext ctx, string? repository, IReadOnlyDictionary<string, string>? answers)
    {
        var repo = Clean(repository) ?? ctx.Repository ?? string.Empty;
        var kind = KindOf(ctx, repo);
        return new RenderVars(repo, kind ?? string.Empty, FlowTargetsFor(ctx, kind), BaseFor(ctx, kind),
            string.Join(", ", PromotionTargets(ctx, kind)), answers ?? new Dictionary<string, string>());
    }

    /// <summary>Troca os marcadores de um texto. Desconhecido (ou ask sem resposta) fica como está.</summary>
    public static string RenderText(string? text, WorkItemTemplateContext ctx, RenderVars vars)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{')) return text ?? string.Empty;
        return Marker().Replace(text, m =>
        {
            var name = m.Groups["name"].Value;
            var arg = m.Groups["arg"].Success ? m.Groups["arg"].Value.Trim() : null;
            if (arg is not null)
            {
                if (string.Equals(name, "ask", StringComparison.OrdinalIgnoreCase))
                    return vars.Answers.TryGetValue(arg, out var a) && a.Length > 0 ? a : vars.MissingAsk?.Invoke(arg) ?? m.Value;
                if (string.Equals(name, "suffix", StringComparison.OrdinalIgnoreCase))
                {
                    if (!arg.StartsWith('@')) return BranchRules.SuffixFor(arg, ctx.Suffixes);
                    var askKey = arg[1..].Trim();
                    return vars.Answers.TryGetValue(askKey, out var target) && target.Length > 0
                        ? BranchRules.SuffixFor(target, ctx.Suffixes)
                        : vars.MissingSuffix?.Invoke(askKey) ?? m.Value;
                }
                return m.Value;
            }
            return name.ToLowerInvariant() switch
            {
                "card" => ctx.Card,
                "branch" => ctx.MainBranch,
                "repository" => vars.Repository,
                "repokind" => vars.RepoKind,
                "flow" => ctx.Flow ?? string.Empty,
                "flowtargets" => vars.FlowTargets,
                "base" => vars.Base,
                "qabranch" => QaBranch(ctx),
                "promotiontargets" => vars.PromotionTargets,
                "ticketsystem" => TicketSystem(ctx),
                "each" => vars.Each ?? m.Value,
                "eachlabel" => vars.EachLabel ?? vars.Each ?? m.Value,
                "validationenvironments" => string.Join(", ", ctx.Environments.Select(e => e.Key)),
                "validationdefaults" => ValidationDefaults(ctx),
                _ => m.Value
            };
        });
    }

    /// <summary>0092: ambientes sugeridos para o tipo do card (<c>defaults</c> da configuração; sem tipo: os do bug).</summary>
    public static string ValidationDefaults(WorkItemTemplateContext ctx)
    {
        var type = ctx.Type ?? "bug";
        return ctx.EnvironmentDefaults.TryGetValue(type, out var v) ? v
            : ctx.EnvironmentDefaults.TryGetValue("bug", out var b) ? b : string.Empty;
    }

    /// <summary>Marcadores que o renderizador não conhece no texto (para conferir modelos semeados/admin).</summary>
    public static IReadOnlyList<string> UnknownMarkers(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        return Marker().Matches(text)
            .Where(m => m.Groups["arg"].Success
                ? !(m.Groups["name"].Value.Equals("ask", StringComparison.OrdinalIgnoreCase) || m.Groups["name"].Value.Equals("suffix", StringComparison.OrdinalIgnoreCase))
                : !SimpleMarkers.Contains(m.Groups["name"].Value))
            .Select(m => m.Value)
            .ToList();
    }

    /// <summary>Troca os marcadores em todos os textos de um nó (objetos e listas), menos nas chaves <c>key</c> e <c>repeat</c>
    /// (0092: o <c>repeat</c> fica cru para a tela expandir a prévia com as respostas da pessoa).</summary>
    public static void ExpandStrings(JsonNode node, Func<string, string> expand)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (key is "key" or "repeat") continue;
                    if (obj[key] is JsonValue v && v.TryGetValue<string>(out var s))
                        obj[key] = expand(s);
                    else if (obj[key] is { } child)
                        ExpandStrings(child, expand);
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue v && v.TryGetValue<string>(out var s))
                        arr[i] = expand(s);
                    else if (arr[i] is { } child)
                        ExpandStrings(child, expand);
                }
                break;
        }
    }

    // ── Util ─────────────────────────────────────────────────────────────────────────────────────

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string? Str(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static List<string> Strings(JsonNode? node) => node switch
    {
        JsonArray arr => arr.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s.Trim() : null)
            .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList(),
        JsonValue v when v.TryGetValue<string>(out var s) => SplitList(s),
        _ => []
    };

    [GeneratedRegex(@"\{(?<name>[A-Za-z][A-Za-z0-9]*)(?::(?<arg>[^{}]+))?\}")]
    private static partial Regex Marker();
}
