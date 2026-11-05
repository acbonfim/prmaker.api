using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using solvace.azure.application.Contract;
using solvace.executionplans.application.Contracts;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Extensions;
using solvace.prform.domain.Promotion;

namespace solvace.prform.Skills;

/// <summary>Um PR do fluxo do card para um tipo de repositório: branch de origem (já com o sufixo), de onde ela sai e o destino.</summary>
public record CardFlowPr(string Branch, string? From, string Target);

/// <summary>Fluxo de um tipo de repositório (BranchStrategy.flows[flow][kind]).</summary>
public record CardFlowKind(string Kind, IReadOnlyList<string> Repositories, string? Base, IReadOnlyList<string> BaseOptions, bool AskBase,
    IReadOnlyList<CardFlowPr> Prs);

/// <summary>
/// 0085: o que as regras do PRMake dizem para um card — tipo (Bug/US), área e fluxo, branch principal, branches por
/// destino, fluxo por tipo de repositório e padrões. Base do <c>GET Skills/rules</c>, do <c>prmake_rules</c>, do
/// contexto do especialista e do catálogo de modelos de itens.
/// </summary>
public record CardRules(
    string Card,
    string? WorkItemType,
    bool IsUserStory,
    string? Title,
    string? AreaPath,
    string? Flow,
    string? FlowRule,
    string MainBranch,
    string BranchNamePattern,
    IReadOnlyDictionary<string, string> Suffixes,
    IReadOnlyList<CardFlowKind> Kinds,
    string? DefaultRepository,
    string? DefaultKind,
    string CommitPattern,
    string PrTitlePattern,
    string PromotionBranchPattern,
    IReadOnlyDictionary<string, string> Settings,
    string? CardError,
    string? State = null)
{
    /// <summary>Destinos dos PRs do fluxo para o repositório padrão (ex.: "development, qa"; release: "release-version/hotfix-version").</summary>
    public string FlowTargets()
    {
        var kind = Kinds.FirstOrDefault(k => string.Equals(k.Kind, DefaultKind, StringComparison.OrdinalIgnoreCase)) ?? Kinds.FirstOrDefault();
        if (kind is null) return string.Empty;
        var targets = kind.Prs.Select(p => p.Target.Contains("{base}", StringComparison.OrdinalIgnoreCase) && kind.BaseOptions.Count > 0
                ? string.Join("/", kind.BaseOptions)
                : p.Target)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", targets);
    }

    /// <summary>Marcadores extras dos modelos de itens ({repository}, {flowTargets}, {flow}).</summary>
    public IReadOnlyDictionary<string, string> TemplateExtras() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["repository"] = DefaultRepository ?? string.Empty,
        ["flowTargets"] = FlowTargets(),
        ["flow"] = Flow ?? string.Empty
    };
}

/// <summary>
/// 0085: "Regras do PRMake" calculadas para o card — o especialista e as skills leem daqui em vez de inventar nome de
/// branch. Tudo vem do "Skills Configurations" (BranchFlowByArea, BranchStrategy, BranchNamePattern,
/// BranchSuffixByTarget, padrões) e do card no DevOps (área, tipo). Cache curto por card.
/// </summary>
public class SkillsRulesService(
    IPluginConfigurationResolver resolver,
    IAzureService azure,
    IMemoryCache cache,
    ILogger<SkillsRulesService> logger) : IPrmakeRulesProvider
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    public async Task<CardRules> GetAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var card = NormalizeCard(cardNumber);
        if (card.Length == 0)
            throw new ArgumentException("Informe o número do card.");
        var key = "prmake-rules:" + card;
        if (cache.TryGetValue(key, out CardRules? cached) && cached is not null)
            return cached;
        var rules = await BuildAsync(card, cancellationToken);
        // Sem o card (DevOps fora/sem integração) guarda por pouco tempo: a próxima chamada tenta de novo.
        cache.Set(key, rules, rules.CardError is null ? CacheFor : TimeSpan.FromSeconds(15));
        return rules;
    }

    public async Task<string?> RulesMarkdownAsync(string cardNumber, bool summary, CancellationToken cancellationToken)
    {
        try
        {
            var rules = await GetAsync(cardNumber, cancellationToken);
            return summary ? SummaryMarkdown(rules) : Markdown(rules);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Regras do PRMake indisponíveis para o card {Card}", cardNumber);
            return null;
        }
    }

    public async Task<bool?> IsUserStoryAsync(string cardNumber, CancellationToken cancellationToken)
    {
        try
        {
            var rules = await GetAsync(cardNumber, cancellationToken);
            return rules.WorkItemType is null ? null : rules.IsUserStory;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<CardRules> BuildAsync(string card, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        string V(string k, string fallback = "") => settings.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

        string? type = null, title = null, area = null, state = null, cardError = null;
        try
        {
            var wi = await azure.GetCardAsync(card, cancellationToken);
            if (wi is null || !string.IsNullOrWhiteSpace(wi.Error))
                cardError = wi?.Error is { Length: > 0 } err ? err : "card não encontrado no DevOps";
            else
            {
                type = wi.Fields.WorkItemType;
                title = wi.Fields.Title;
                area = wi.Fields.AreaPath;
                state = wi.Fields.State;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            cardError = e.Message;
        }

        var pattern = V(SkillsConfigurationKeys.BranchNamePattern, "hotfix/{card}");
        var main = BranchRules.MainBranch(pattern, card);
        var suffixes = BranchRules.ParseSuffixes(V(SkillsConfigurationKeys.BranchSuffixByTarget));
        var (flow, flowRule) = FlowOf(area, V(SkillsConfigurationKeys.BranchFlowByArea));
        var strategy = V(SkillsConfigurationKeys.BranchStrategy);
        var defaultRepo = V(SkillsConfigurationKeys.DefaultRepository);
        var kinds = flow is null ? [] : KindsOf(strategy, flow, main);

        return new CardRules(card, type, IsUserStory(type), title, area, flow, flowRule, main, pattern, suffixes, kinds,
            defaultRepo.Length > 0 ? defaultRepo : null,
            defaultRepo.Length > 0 ? BranchStrategyKinds.KindOf(defaultRepo, strategy) : null,
            V(SkillsConfigurationKeys.CommitMessagePattern, "AB#{card} {summary}"),
            V(SkillsConfigurationKeys.PrTitlePattern, "AB#{card} {TARGET}"),
            V(SkillsConfigurationKeys.PromotionBranchPattern, "{branch}-{target}"),
            settings, cardError, state);
    }

    public static bool IsUserStory(string? workItemType) =>
        string.Equals(workItemType?.Trim(), "User Story", StringComparison.OrdinalIgnoreCase);

    /// <summary>Só o "Skills Configurations" (sem card) — catálogo sem card.</summary>
    public Task<IReadOnlyDictionary<string, string>> SettingsOnlyAsync(CancellationToken cancellationToken) => SettingsAsync(cancellationToken);

    private async Task<IReadOnlyDictionary<string, string>> SettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = await resolver.GetEffectiveConfigurationAsync(SkillsConfigurationKeys.PluginName, cancellationToken);
            var values = string.IsNullOrWhiteSpace(config.Options) ? null : config.Options.JsonToListOfDictionaries().FirstOrDefault();
            return new Dictionary<string, string>(values ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            logger.LogWarning("Plugin {Plugin} indisponível: {Error} — regras com os padrões.", SkillsConfigurationKeys.PluginName, e.Message);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Fluxo pela área do card (BranchFlowByArea: [{areaContains, flow}], primeira que casa). Null = perguntar.</summary>
    public static (string? Flow, string? Rule) FlowOf(string? areaPath, string? json)
    {
        if (string.IsNullOrWhiteSpace(areaPath) || string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return (null, null);
            foreach (var rule in doc.RootElement.EnumerateArray())
            {
                var contains = Str(rule, "areaContains");
                var flow = Str(rule, "flow");
                if (contains is { Length: > 0 } && flow is { Length: > 0 }
                    && areaPath.Contains(contains, StringComparison.OrdinalIgnoreCase))
                    return (flow, contains);
            }
        }
        catch (JsonException)
        {
        }
        return (null, null);
    }

    /// <summary>Tipos de repositório do fluxo com base, opções de base e os PRs (branch de origem já calculada).</summary>
    public static List<CardFlowKind> KindsOf(string? strategyJson, string flow, string mainBranch)
    {
        var result = new List<CardFlowKind>();
        if (string.IsNullOrWhiteSpace(strategyJson)) return result;
        try
        {
            using var doc = JsonDocument.Parse(strategyJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;
            var repos = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("repositories", out var r) && r.ValueKind == JsonValueKind.Array)
                foreach (var rule in r.EnumerateArray())
                    if (Str(rule, "kind") is { Length: > 0 } k && Str(rule, "match") is { Length: > 0 } m)
                    {
                        if (!repos.TryGetValue(k, out var list)) repos[k] = list = [];
                        list.Add(m);
                    }
            if (!root.TryGetProperty("flows", out var flows) || flows.ValueKind != JsonValueKind.Object
                || !flows.TryGetProperty(flow, out var f) || f.ValueKind != JsonValueKind.Object)
                return result;
            foreach (var kind in f.EnumerateObject())
            {
                if (kind.Value.ValueKind != JsonValueKind.Object) continue;
                var v = kind.Value;
                var options = v.TryGetProperty("baseOptions", out var bo) && bo.ValueKind == JsonValueKind.Array
                    ? bo.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                    : [];
                var ask = v.TryGetProperty("askBase", out var a) && a.ValueKind == JsonValueKind.True;
                var prs = new List<CardFlowPr>();
                if (v.TryGetProperty("prs", out var p) && p.ValueKind == JsonValueKind.Array)
                    foreach (var pr in p.EnumerateArray())
                        prs.Add(new CardFlowPr(mainBranch + (Str(pr, "suffix") ?? string.Empty), Str(pr, "from"), Str(pr, "target") ?? "?"));
                result.Add(new CardFlowKind(kind.Name, repos.TryGetValue(kind.Name, out var rl) ? rl : [], Str(v, "base"), options, ask, prs));
            }
        }
        catch (JsonException)
        {
        }
        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;

    /// <summary>
    /// 0085 r3: bases e destinos do fluxo (para ordenar o autocomplete de branches) — master/main, as bases, opções de base e
    /// destinos dos PRs de todos os fluxos do BranchStrategy e os destinos do BranchSuffixByTarget, sem repetir, nessa ordem.
    /// Destino com marcador (<c>{base}</c>) fica de fora.
    /// </summary>
    public static List<string> FlowBaseBranches(string? strategyJson, IReadOnlyDictionary<string, string> suffixes)
    {
        var result = new List<string>();
        void Add(string? name)
        {
            var v = name?.Trim();
            if (string.IsNullOrEmpty(v) || v.Contains('{') || result.Contains(v, StringComparer.OrdinalIgnoreCase)) return;
            result.Add(v);
        }

        Add("master");
        Add("main");
        if (!string.IsNullOrWhiteSpace(strategyJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(strategyJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("flows", out var flows) && flows.ValueKind == JsonValueKind.Object)
                    foreach (var flow in flows.EnumerateObject())
                    {
                        if (flow.Value.ValueKind != JsonValueKind.Object) continue;
                        foreach (var kind in flow.Value.EnumerateObject())
                        {
                            var v = kind.Value;
                            if (v.ValueKind != JsonValueKind.Object) continue;
                            Add(Str(v, "base"));
                            if (v.TryGetProperty("baseOptions", out var bo) && bo.ValueKind == JsonValueKind.Array)
                                foreach (var o in bo.EnumerateArray())
                                    if (o.ValueKind == JsonValueKind.String) Add(o.GetString());
                            if (v.TryGetProperty("prs", out var prs) && prs.ValueKind == JsonValueKind.Array)
                                foreach (var pr in prs.EnumerateArray())
                                    Add(Str(pr, "target"));
                        }
                    }
            }
            catch (JsonException)
            {
            }
        }
        foreach (var target in suffixes.Keys)
            Add(target);
        return result;
    }

    public static string NormalizeCard(string? card)
    {
        var v = (card ?? string.Empty).Trim();
        if (v.StartsWith("AB#", StringComparison.OrdinalIgnoreCase)) v = v[3..];
        return v.TrimStart('#').Trim();
    }

    // ── Markdown ─────────────────────────────────────────────────────────────────────────────────

    private static string TypeText(CardRules r) => r.IsUserStory ? "User Story" : r.WorkItemType is { Length: > 0 } t ? t : "Bug (tipo não confirmado no DevOps)";

    private static void AppendTypeChanges(StringBuilder sb, CardRules r)
    {
        if (r.IsUserStory)
            sb.AppendLine("- **User Story**: análise pela skill `analisar-us` (plano com Kind `analisar-us`), texto do card = critérios de aceite, item da análise `analysis` e da implementação `implementation`, **sem Root Cause** no DevOps.");
        else
            sb.AppendLine("- **Bug**: análise pela skill `analisar-bug`, texto do card = Repro Steps, item da análise `analysis` e da correção `fix`, Root Cause no DevOps ao gerar o PR.");
        sb.AppendLine("- Tipo do card (Bug/US, do DevOps) não é o tipo do item de trabalho: itens têm tipos próprios (`WorkItemTypes`: task, pull-request, review, adjustment, closing, data, ticket, text...). Nunca crie item como `bug`/`user-story` (US é uma história do DevOps).");
    }

    private static void AppendBranchesByTarget(StringBuilder sb, CardRules r)
    {
        sb.AppendLine("| Destino | Branch de trabalho |");
        sb.AppendLine("|---|---|");
        foreach (var (target, _) in r.Suffixes)
            sb.AppendLine($"| {target} | `{BranchRules.BranchFor(r.MainBranch, target, r.Suffixes)}` |");
        sb.AppendLine($"| outro destino | `{r.MainBranch}-<destino>` |");
    }

    private static readonly string[] FixedRules =
    [
        "Nunca mesclar PR: só abrir (o merge é sempre humano).",
        "Toda gravação (plano, itens, PRs, card no DevOps, Timeline, arquivos) passa pelo PRMake — MCP `prmake_*` ou `prmake-plan.sh`; nada direto no GitHub/DevOps.",
        "O Especialista do caso não altera o plano: propõe (`prmake_case_propose`) e uma pessoa aceita.",
        "Nunca invente nome de branch: use a branch principal e a tabela de branches por destino acima.",
        "Itens de trabalho: antes de criar, liste (`prmake_work_items`) e confira se já existe item com o mesmo objetivo (fingerprint/status) — o PRMake recusa duplicata; `allowDuplicate` + motivo só quando for de fato outro trabalho.",
        "Itens de trabalho: edição só com o item `pending` (todas as sub-tarefas `pending`); iniciado/concluído → criar um novo a partir dele.",
        "Sub-tarefas de um item rodam em ordem; itens diferentes rodam em paralelo. Branch nunca é reaproveitada e nunca há force-push."
    ];

    public static string Markdown(CardRules r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Regras do PRMake — card {r.Card}");
        sb.AppendLine();
        if (r.Title is { Length: > 0 }) sb.AppendLine($"**Card:** {r.Title}");
        sb.AppendLine($"**Tipo:** {TypeText(r)}");
        sb.AppendLine($"**Área:** {r.AreaPath ?? "(não lida)"} → **fluxo:** " +
                      (r.Flow is null ? "não identificado — pergunte ao usuário qual fluxo (BranchFlowByArea sem regra para a área)" : $"`{r.Flow}` (regra \"{r.FlowRule}\")"));
        if (r.CardError is { Length: > 0 }) sb.AppendLine($"> Não consegui ler o card no DevOps ({r.CardError}) — tipo e fluxo podem estar incompletos.");

        sb.AppendLine().AppendLine("## O que muda pelo tipo do card");
        AppendTypeChanges(sb, r);

        sb.AppendLine().AppendLine("## Branches");
        sb.AppendLine($"- Branch principal do card: `{r.MainBranch}` (BranchNamePattern `{r.BranchNamePattern}`).");
        sb.AppendLine("- Nome da branch por destino (BranchSuffixByTarget) — use exatamente estes:");
        sb.AppendLine();
        AppendBranchesByTarget(sb, r);
        sb.AppendLine();
        sb.AppendLine($"- Promoção/cherry-pick para outra versão: padrão `{r.PromotionBranchPattern}` (ex.: release-version → `{PromotionExample(r, "release-version")}`).");

        sb.AppendLine().AppendLine(r.Flow is null ? "## Fluxo por tipo de repositório" : $"## Fluxo `{r.Flow}` por tipo de repositório (BranchStrategy)");
        if (r.Kinds.Count == 0)
            sb.AppendLine("- Sem fluxo calculado (área sem regra ou BranchStrategy sem o fluxo): pergunte ao usuário a base e os destinos.");
        foreach (var k in r.Kinds)
        {
            var repos = k.Repositories.Count > 0 ? $" (repositórios: {string.Join(", ", k.Repositories)})" : string.Empty;
            var baseText = k.Base is { Length: > 0 } b ? $"base `{b}`"
                : k.BaseOptions.Count > 0 ? $"base a escolher entre {string.Join(", ", k.BaseOptions.Select(o => $"`{o}`"))}{(k.AskBase ? " — pergunte ao usuário" : string.Empty)}"
                : "base não configurada";
            sb.AppendLine($"- **{k.Kind}**{repos}: {baseText}.");
            foreach (var pr in k.Prs)
                sb.AppendLine($"  - PR `{pr.Branch}`{(pr.From is { Length: > 0 } f ? $" (cortada de `{f}`)" : string.Empty)} → `{pr.Target}`");
        }
        if (r.DefaultRepository is { Length: > 0 } repo)
            sb.AppendLine($"- Repositório padrão (sem git remote): `{repo}`{(r.DefaultKind is { } dk ? $" ({dk})" : string.Empty)}.");

        sb.AppendLine().AppendLine("## Padrões");
        sb.AppendLine($"- Commit: `{r.CommitPattern.Replace("{card}", r.Card, StringComparison.OrdinalIgnoreCase)}`");
        sb.AppendLine($"- Título do PR: `{r.PrTitlePattern.Replace("{card}", r.Card, StringComparison.OrdinalIgnoreCase)}` ({{TARGET}} = destino em maiúsculas)");

        sb.AppendLine().AppendLine("## Regras fixas");
        foreach (var rule in FixedRules) sb.AppendLine($"- {rule}");
        return sb.ToString();
    }

    /// <summary>Resumo para o contexto do Especialista do caso (o completo vem do prmake_rules).</summary>
    public static string SummaryMarkdown(CardRules r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"- Tipo: {TypeText(r)} · área: {r.AreaPath ?? "?"} · fluxo: {(r.Flow is null ? "não identificado (perguntar)" : $"`{r.Flow}`")}");
        sb.AppendLine($"- Branch principal: `{r.MainBranch}`. Por destino: " +
                      string.Join(", ", r.Suffixes.Select(s => $"{s.Key} → `{BranchRules.BranchFor(r.MainBranch, s.Key, r.Suffixes)}`")) + ".");
        foreach (var k in r.Kinds)
            sb.AppendLine($"- {k.Kind}: " + string.Join("; ", k.Prs.Select(p => $"`{p.Branch}` → `{p.Target}`")) +
                          (k.BaseOptions.Count > 0 ? $" (base: {string.Join("/", k.BaseOptions)})" : k.Base is { Length: > 0 } b ? $" (base `{b}`)" : string.Empty));
        sb.AppendLine($"- Commit `{r.CommitPattern.Replace("{card}", r.Card, StringComparison.OrdinalIgnoreCase)}` · título do PR `{r.PrTitlePattern.Replace("{card}", r.Card, StringComparison.OrdinalIgnoreCase)}`.");
        sb.AppendLine("- Nunca mesclar PR; toda gravação pelo PRMake; você não altera o plano (propõe). Regras completas: prmake_rules.");
        return sb.ToString();
    }

    private static string PromotionExample(CardRules r, string target)
    {
        var safe = BranchRules.SafeTarget(target);
        return r.PromotionBranchPattern
            .Replace("{branch}", r.MainBranch, StringComparison.OrdinalIgnoreCase)
            .Replace("{suffix}", BranchRules.SuffixFor(target, r.Suffixes), StringComparison.OrdinalIgnoreCase)
            .Replace("{target}", safe, StringComparison.OrdinalIgnoreCase);
    }
}
