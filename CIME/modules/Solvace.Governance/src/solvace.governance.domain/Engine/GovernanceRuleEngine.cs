using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using solvace.governance.domain.Entities;

namespace solvace.governance.domain.Engine;

/// <summary>O que está sendo avaliado (HTTP, MCP ou hook do Claude Code).</summary>
public sealed record GovernanceRequest
{
    public string Source { get; init; } = GovernanceSource.Api;
    /// <summary>'POST ExecutionPlan/{id}/steps/{key}/complete', 'mcp:prmake_advance', 'hook:PreToolUse:Bash'.</summary>
    public string? Action { get; init; }
    /// <summary>Ferramenta do Claude Code (Bash, Write, mcp__prmake__prmake_advance…).</summary>
    public string? Tool { get; init; }
    /// <summary>Comando/entrada da ferramenta ou corpo resumido.</summary>
    public string? Input { get; init; }
    public string? Path { get; init; }
    public Guid? UserId { get; init; }
    public string? UserName { get; init; }
    public IReadOnlyCollection<string> Roles { get; init; } = [];
    public string? CardType { get; init; }
}

public sealed record GovernanceRuleHit(string Key, string Name, string Effect, string Message);

public sealed record GovernanceDecision(
    string Decision,
    string Mode,
    string? Reason,
    IReadOnlyList<GovernanceRuleHit> Rules,
    string WouldDecide)
{
    public static GovernanceDecision Allow(string mode) =>
        new(GovernanceDecisionKind.Allow, mode, null, [], GovernanceDecisionKind.Allow);

    public bool Blocked => Decision == GovernanceDecisionKind.Block;

    /// <summary>Resultado do evento na trilha.</summary>
    public string Outcome => Decision switch
    {
        GovernanceDecisionKind.Block => GovernanceOutcome.Blocked,
        GovernanceDecisionKind.Warn => GovernanceOutcome.Warned,
        _ => Rules.Count > 0 ? GovernanceOutcome.Monitored : GovernanceOutcome.Ok
    };
}

/// <summary>
/// 0098: motor único das regras de governança (puro: sem banco nem rede). Uma regra casa quando está ativa, o escopo
/// vale e cada dimensão preenchida casa; regra sem dimensão nunca casa. Decisão = efeito mais forte; no modo monitor a
/// decisão é sempre allow (o evento sai "monitored") e <see cref="GovernanceDecision.WouldDecide"/> diz o que o enforce faria.
/// </summary>
public static class GovernanceRuleEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new();
    private static readonly ConcurrentDictionary<string, Regex> GlobCache = new();

    public static GovernanceDecision Evaluate(GovernanceRequest request, IEnumerable<GovernanceRule> rules, GovernanceSettings settings)
    {
        if (settings.Mode == GovernanceMode.Off) return GovernanceDecision.Allow(settings.Mode);

        var roles = request.Roles.Select(r => r.ToLowerInvariant()).ToHashSet();
        if (settings.ExemptRoles.Count > 0 && settings.ExemptRoles.Any(roles.Contains))
            return GovernanceDecision.Allow(settings.Mode);

        var hits = rules
            .Where(r => r.Enabled && Matches(r, request, roles))
            .OrderByDescending(r => GovernanceEffect.Weight(r.Effect))
            .ThenBy(r => r.Order)
            .Select(r => new GovernanceRuleHit(r.Key, r.Name, r.Effect, MessageOf(r)))
            .ToList();
        if (hits.Count == 0) return GovernanceDecision.Allow(settings.Mode);

        var strongest = hits[0];
        var would = GovernanceEffect.Weight(strongest.Effect) switch
        {
            3 => GovernanceDecisionKind.Block,
            2 => GovernanceDecisionKind.Warn,
            _ => GovernanceDecisionKind.Allow
        };
        var decision = settings.Mode == GovernanceMode.Enforce ? would : GovernanceDecisionKind.Allow;
        var reason = would == GovernanceDecisionKind.Allow ? null : strongest.Message;
        return new GovernanceDecision(decision, settings.Mode, reason, hits, would);
    }

    public static string MessageOf(GovernanceRule rule)
    {
        var msg = string.IsNullOrWhiteSpace(rule.Message) ? rule.Name : rule.Message;
        return rule.Effect == GovernanceEffect.Approval ? $"{msg} (precisa de aprovação de um administrador)" : msg;
    }

    public static bool Matches(GovernanceRule rule, GovernanceRequest request, IReadOnlySet<string>? roles = null)
    {
        roles ??= request.Roles.Select(r => r.ToLowerInvariant()).ToHashSet();
        if (!ScopeApplies(rule.Scope, request, roles)) return false;

        var m = rule.Match;
        if (m.IsEmpty) return false;
        if (m.Actions.Count > 0 && (request.Action is null || !m.Actions.Any(g => Glob(g, request.Action)))) return false;
        if (m.Tools.Count > 0 && (request.Tool is null || !m.Tools.Any(g => Glob(g, request.Tool)))) return false;
        if (m.Paths.Count > 0 && (request.Path is null || !m.Paths.Any(g => PathGlob(g, request.Path)))) return false;
        var input = request.Input ?? string.Empty;
        if (m.Patterns.Count > 0 && !m.Patterns.Any(p => RegexMatch(p, input))) return false;
        if (m.AllOf.Count > 0 && !m.AllOf.All(p => RegexMatch(p, input))) return false;
        return true;
    }

    private static bool ScopeApplies(RuleScope scope, GovernanceRequest request, IReadOnlySet<string> roles)
    {
        if (scope.ExceptRoles.Count > 0 && scope.ExceptRoles.Any(roles.Contains)) return false;
        if (scope.Roles.Count > 0 && !scope.Roles.Any(roles.Contains)) return false;
        if (scope.Sources.Count > 0 && !scope.Sources.Contains(request.Source)) return false;
        if (scope.CardTypes.Count > 0 && (request.CardType is null || !scope.CardTypes.Contains(request.CardType.ToLowerInvariant()))) return false;
        if (scope.Users.Count > 0)
        {
            var id = request.UserId?.ToString().ToLowerInvariant();
            var name = request.UserName?.Trim().ToLowerInvariant();
            if (!scope.Users.Any(u => u == id || u == name)) return false;
        }
        return true;
    }

    /// <summary>Regex do admin, sem diferenciar maiúsculas, com tempo máximo (regex inválida ou lenta = não casa).</summary>
    public static bool RegexMatch(string pattern, string input)
    {
        var rx = RegexCache.GetOrAdd(pattern, p =>
        {
            try { return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout); }
            catch (ArgumentException) { return null; }
        });
        if (rx is null) return false;
        try { return rx.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>Glob simples sem diferenciar maiúsculas: '*' = qualquer sequência, '?' = um caractere, '|' separa alternativas.</summary>
    public static bool Glob(string glob, string value)
    {
        if (string.IsNullOrEmpty(glob)) return true;
        var rx = GlobCache.GetOrAdd("g:" + glob, g => new Regex(
            "^(?:" + string.Join("|", g[2..].Split('|').Select(part => Regex.Escape(part.Trim()).Replace(@"\*", ".*").Replace(@"\?", "."))) + ")$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline));
        return rx.IsMatch(value);
    }

    /// <summary>Glob de caminho: '**' = qualquer sequência (com barras), '*' = sem barra; '~' casa com a pasta do usuário.</summary>
    public static bool PathGlob(string glob, string path)
    {
        var p = path.Replace('\\', '/');
        var rx = GlobCache.GetOrAdd("p:" + glob, g =>
        {
            var body = Regex.Escape(g[2..].Replace('\\', '/'))
                .Replace(@"\*\*/", "(?:.*/)?")
                .Replace(@"\*\*", ".*")
                .Replace(@"\*", "[^/]*")
                .Replace(@"\?", "[^/]");
            if (body.StartsWith("~")) body = "(?:~|.*)" + body[1..];
            return new Regex("^" + body + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        });
        return rx.IsMatch(p);
    }
}
