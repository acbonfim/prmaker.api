using System.Text.Json;
using solvace.governance.application.Contracts;
using solvace.governance.domain;
using solvace.governance.domain.Engine;
using solvace.governance.domain.Entities;

namespace solvace.governance.application;

public interface IGovernanceApplication
{
    Task<GovernanceDecision> EvaluateAsync(GovernanceRequest request, CancellationToken ct = default);
    Task<HooksConfigResponse> GetHooksConfigAsync(CancellationToken ct = default);

    Task<List<GovernanceRule>> GetRulesAsync(CancellationToken ct = default);
    Task<GovernanceRule> CreateRuleAsync(RuleRequest request, GovernanceActor actor, CancellationToken ct = default);
    Task<GovernanceRule?> UpdateRuleAsync(Guid id, RuleRequest request, GovernanceActor actor, CancellationToken ct = default);
    Task<bool?> DeleteRuleAsync(Guid id, GovernanceActor actor, CancellationToken ct = default);
    Task<EvaluateResponse> TestRuleAsync(RuleTestRequest request, GovernanceActor actor, CancellationToken ct = default);

    Task<List<GovernanceHook>> GetHooksAsync(CancellationToken ct = default);
    Task<GovernanceHook> CreateHookAsync(HookRequest request, GovernanceActor actor, CancellationToken ct = default);
    Task<GovernanceHook?> UpdateHookAsync(Guid id, HookRequest request, GovernanceActor actor, CancellationToken ct = default);
    Task<bool?> DeleteHookAsync(Guid id, GovernanceActor actor, CancellationToken ct = default);

    Task<GovernanceSettings> GetSettingsAsync(CancellationToken ct = default);
    Task<GovernanceSettings> UpdateSettingsAsync(SettingsRequest request, GovernanceActor actor, CancellationToken ct = default);

    Task<(int Total, List<GovernanceEvent> Items)> GetEventsAsync(GovernanceEventFilter filter, CancellationToken ct = default);
}

/// <summary>
/// 0098: regras, hooks e configurações da governança. Toda alteração vira um evento da trilha (categoria
/// "governance", com o antes/depois resumido) e invalida a fotografia em memória desta instância.
/// </summary>
public class GovernanceApplication(
    IGovernanceRepository repository,
    IGovernancePolicyProvider policy,
    IGovernanceEventSink sink,
    TimeProvider clock) : IGovernanceApplication
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<GovernanceDecision> EvaluateAsync(GovernanceRequest request, CancellationToken ct = default)
    {
        var p = await policy.GetAsync(ct);
        return GovernanceRuleEngine.Evaluate(request, p.Rules, p.Settings);
    }

    public async Task<HooksConfigResponse> GetHooksConfigAsync(CancellationToken ct = default)
    {
        var p = await policy.GetAsync(ct);
        var hooks = p.Settings.Mode == GovernanceMode.Off
            ? []
            : p.Hooks.Where(h => h.Enabled && h.Event.StartsWith(GovernanceHookEvent.ClaudePrefix))
                .OrderBy(h => h.Order)
                .Select(h => new HookConfigItem(h.Key, h.Event, h.Matcher, h.Action, h.PublicConfig()))
                .ToList();
        // O script só chama o evaluate quando a ferramenta casa com alguma regra ativa (ou com regra sem ferramenta).
        // Regra só de ações do servidor (POST ..., mcp:...) nunca casa num hook: fica fora (evita ida à rede à toa).
        var matchers = p.Rules.Where(r => r.Enabled && !r.Match.IsEmpty && CanMatchHook(r.Match))
            .SelectMany(r => r.Match.Tools.Count > 0 ? r.Match.Tools : ["*"])
            .Distinct()
            .ToList();
        return new HooksConfigResponse(p.Settings.Mode, p.Settings.HookFailOpen, p.Settings.HookTimeoutMs,
            p.Settings.HookCacheSeconds, hooks, matchers, p.Version);
    }

    public Task<List<GovernanceRule>> GetRulesAsync(CancellationToken ct = default) => repository.GetRulesAsync(ct);

    /// <summary>A ação de um hook é "hook:&lt;evento&gt;:&lt;ferramenta&gt;": sem glob que possa casar com isso, a regra é do servidor.</summary>
    public static bool CanMatchHook(RuleMatch m) =>
        m.Actions.Count == 0 || m.Actions.Any(a => a.Split('|').Any(part => part.Trim() is var g && (g.StartsWith('*') || g.StartsWith("hook", StringComparison.OrdinalIgnoreCase))));

    public async Task<GovernanceRule> CreateRuleAsync(RuleRequest request, GovernanceActor actor, CancellationToken ct = default)
    {
        var key = GovernanceRule.NormalizeKey(request.Key);
        if (await repository.RuleKeyExistsAsync(key, ct)) throw new DomainException($"Já existe uma regra com a chave {key}.");
        var rule = new GovernanceRule(Guid.NewGuid(), key, builtin: false);
        Apply(rule, request, actor);
        repository.AddRule(rule);
        await repository.SaveChangesAsync(ct);
        Changed(actor, "POST Governance/rules", $"Criou a regra {rule.Key}", null, Snapshot(rule));
        return rule;
    }

    public async Task<GovernanceRule?> UpdateRuleAsync(Guid id, RuleRequest request, GovernanceActor actor, CancellationToken ct = default)
    {
        var rule = await repository.GetRuleAsync(id, ct);
        if (rule is null) return null;
        var before = Snapshot(rule);
        Apply(rule, request, actor);
        await repository.SaveChangesAsync(ct);
        Changed(actor, "PUT Governance/rules/{id}", $"Alterou a regra {rule.Key}", before, Snapshot(rule));
        return rule;
    }

    public async Task<bool?> DeleteRuleAsync(Guid id, GovernanceActor actor, CancellationToken ct = default)
    {
        var rule = await repository.GetRuleAsync(id, ct);
        if (rule is null) return null;
        if (rule.Builtin) return false;
        var before = Snapshot(rule);
        repository.RemoveRule(rule);
        await repository.SaveChangesAsync(ct);
        Changed(actor, "DELETE Governance/rules/{id}", $"Apagou a regra {rule.Key}", before, null);
        return true;
    }

    public async Task<EvaluateResponse> TestRuleAsync(RuleTestRequest request, GovernanceActor actor, CancellationToken ct = default)
    {
        var req = ToEngineRequest(request.ToEvaluate(), request.Source, actor with { Roles = request.Roles ?? actor.Roles });
        if (request.Rule is null) return EvaluateResponse.From(await EvaluateAsync(req, ct));

        // Rascunho: avaliado sozinho, ativo e em enforce (o admin quer ver o efeito da regra, não o modo atual).
        var draft = new GovernanceRule(Guid.Empty, string.IsNullOrWhiteSpace(request.Rule.Key) ? "rascunho" : request.Rule.Key, builtin: false);
        Apply(draft, request.Rule with { Enabled = true }, actor);
        var settings = new GovernanceSettings();
        settings.Update(GovernanceMode.Enforce, settings.RetentionDays, true, settings.HookTimeoutMs, settings.HookCacheSeconds,
            [], GovernanceSource.All, settings.Milestones, actor.Name, clock.GetUtcNow());
        return EvaluateResponse.From(GovernanceRuleEngine.Evaluate(req, [draft], settings));
    }

    public Task<List<GovernanceHook>> GetHooksAsync(CancellationToken ct = default) => repository.GetHooksAsync(ct);

    public async Task<GovernanceHook> CreateHookAsync(HookRequest request, GovernanceActor actor, CancellationToken ct = default)
    {
        var key = GovernanceRule.NormalizeKey(request.Key);
        if (await repository.HookKeyExistsAsync(key, ct)) throw new DomainException($"Já existe um hook com a chave {key}.");
        var hook = new GovernanceHook(Guid.NewGuid(), key, builtin: false);
        hook.Update(request.Name, request.Description, request.Event, request.Matcher, request.Action, request.Config,
            request.Enabled, request.Order, actor.Name, clock.GetUtcNow());
        repository.AddHook(hook);
        await repository.SaveChangesAsync(ct);
        Changed(actor, "POST Governance/hooks", $"Criou o hook {hook.Key}", null, HookSnapshot(hook));
        return hook;
    }

    public async Task<GovernanceHook?> UpdateHookAsync(Guid id, HookRequest request, GovernanceActor actor, CancellationToken ct = default)
    {
        var hook = await repository.GetHookAsync(id, ct);
        if (hook is null) return null;
        var before = HookSnapshot(hook);
        // Embutido: o evento e a ação ficam (o script do Claude Code depende deles); o resto o admin muda.
        var evt = hook.Builtin ? hook.Event : request.Event;
        var action = hook.Builtin ? hook.Action : request.Action;
        hook.Update(request.Name, request.Description, evt, request.Matcher, action, MergeSecrets(hook.Config, request.Config),
            request.Enabled, request.Order, actor.Name, clock.GetUtcNow());
        await repository.SaveChangesAsync(ct);
        Changed(actor, "PUT Governance/hooks/{id}", $"Alterou o hook {hook.Key}", before, HookSnapshot(hook));
        return hook;
    }

    public async Task<bool?> DeleteHookAsync(Guid id, GovernanceActor actor, CancellationToken ct = default)
    {
        var hook = await repository.GetHookAsync(id, ct);
        if (hook is null) return null;
        if (hook.Builtin) return false;
        var before = HookSnapshot(hook);
        repository.RemoveHook(hook);
        await repository.SaveChangesAsync(ct);
        Changed(actor, "DELETE Governance/hooks/{id}", $"Apagou o hook {hook.Key}", before, null);
        return true;
    }

    public Task<GovernanceSettings> GetSettingsAsync(CancellationToken ct = default) => repository.GetSettingsAsync(ct);

    public async Task<GovernanceSettings> UpdateSettingsAsync(SettingsRequest request, GovernanceActor actor, CancellationToken ct = default)
    {
        var settings = await repository.GetSettingsAsync(ct);
        var before = Snapshot(settings);
        settings.Update(request.Mode, request.RetentionDays, request.HookFailOpen, request.HookTimeoutMs, request.HookCacheSeconds,
            request.ExemptRoles, request.RecordSources ?? [.. GovernanceSource.All], request.Milestones ?? settings.Milestones,
            actor.Name, clock.GetUtcNow());
        await repository.SaveChangesAsync(ct);
        Changed(actor, "PUT Governance/settings", $"Alterou as configurações (modo {settings.Mode})", before, Snapshot(settings));
        return settings;
    }

    public Task<(int Total, List<GovernanceEvent> Items)> GetEventsAsync(GovernanceEventFilter filter, CancellationToken ct = default) =>
        repository.QueryEventsAsync(filter, ct);

    public static GovernanceRequest ToEngineRequest(EvaluateRequest r, string source, GovernanceActor actor) => new()
    {
        Source = GovernanceSource.IsValid(source) ? source : GovernanceSource.Hook,
        Action = r.Action ?? (r.Tool is null ? null : $"hook:{r.Event ?? "PreToolUse"}:{r.Tool}"),
        Tool = r.Tool,
        Input = r.Input,
        Path = r.Path,
        UserId = actor.UserId,
        UserName = actor.Name,
        Roles = actor.Roles,
        CardType = r.CardType
    };

    private void Apply(GovernanceRule rule, RuleRequest r, GovernanceActor actor) =>
        rule.Update(r.Name, r.Description, r.Category, r.Enabled, r.Effect, r.Order, r.Message, r.Scope, r.Match,
            actor.Name, clock.GetUtcNow());

    /// <summary>A tela recebe segredos mascarados; "***" na volta mantém o valor gravado.</summary>
    private static Dictionary<string, string> MergeSecrets(Dictionary<string, string> current, Dictionary<string, string>? incoming)
    {
        var result = new Dictionary<string, string>(incoming ?? []);
        foreach (var (k, v) in result.ToList())
            if (v == "***" && current.TryGetValue(k, out var old)) result[k] = old;
        return result;
    }

    private void Changed(GovernanceActor actor, string action, string summary, object? before, object? after)
    {
        policy.Invalidate();
        var detail = $"{summary}\nantes: {(before is null ? "-" : JsonSerializer.Serialize(before, Json))}\ndepois: {(after is null ? "-" : JsonSerializer.Serialize(after, Json))}";
        sink.Enqueue(new GovernanceEventData
        {
            StartedAt = clock.GetUtcNow(),
            DurationMs = 0,
            UserId = actor.UserId,
            UserName = actor.Name,
            Role = actor.Roles.FirstOrDefault(),
            Source = GovernanceSource.IsValid(actor.Source) ? actor.Source : GovernanceSource.Screen,
            Action = action,
            Category = "governance",
            Outcome = GovernanceOutcome.Ok,
            Detail = detail
        });
    }

    private static object Snapshot(GovernanceRule r) => new { r.Key, r.Name, r.Enabled, r.Effect, r.Order, r.Message, r.Scope, r.Match };

    private static object Snapshot(GovernanceSettings s) => new { s.Mode, s.RetentionDays, s.HookFailOpen, s.HookTimeoutMs, s.HookCacheSeconds, s.ExemptRoles, s.RecordSources, Milestones = s.Milestones.Count };

    private static object HookSnapshot(GovernanceHook h) => new { h.Key, h.Name, h.Event, h.Matcher, h.Action, Config = h.PublicConfig(), h.Enabled, h.Order };
}
