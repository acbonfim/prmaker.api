using System.Text.RegularExpressions;

namespace solvace.governance.domain.Entities;

/// <summary>
/// 0098: regra/guardrail de governança. Vale no servidor (endpoints e MCP), nas skills (hook PreToolUse do Claude
/// Code) e nas telas. As embutidas (<see cref="Builtin"/>) são semeadas por migração: o admin muda efeito, escopo,
/// casamentos e mensagem, mas não apaga nem troca a chave.
/// </summary>
public class GovernanceRule
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Category { get; private set; } = "fluxo";
    public bool Enabled { get; private set; }
    public string Effect { get; private set; } = GovernanceEffect.Monitor;
    public bool Builtin { get; private set; }
    public int Order { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public RuleScope Scope { get; private set; } = new();
    public RuleMatch Match { get; private set; } = new();
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    protected GovernanceRule() { }

    public GovernanceRule(Guid id, string key, bool builtin)
    {
        Id = id;
        Key = NormalizeKey(key);
        Builtin = builtin;
    }

    private static readonly Regex KeyPattern = new("^[a-z0-9][a-z0-9-]{1,79}$", RegexOptions.Compiled);

    public static string NormalizeKey(string? key)
    {
        var k = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (!KeyPattern.IsMatch(k))
            throw new DomainException("A chave da regra deve ter 2 a 80 caracteres: letras minúsculas, números e hífen.");
        return k;
    }

    public void Update(string name, string? description, string? category, bool enabled, string effect, int order,
        string? message, RuleScope? scope, RuleMatch? match, string actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Informe o nome da regra.");
        if (!GovernanceEffect.IsValid(effect)) throw new DomainException($"Efeito inválido: {effect}.");
        var m = (match ?? new RuleMatch()).Normalized();
        m.Validate();
        Name = name.Trim().Length > 200 ? name.Trim()[..200] : name.Trim();
        Description = (description ?? string.Empty).Trim();
        Category = string.IsNullOrWhiteSpace(category) ? "fluxo" : category.Trim().ToLowerInvariant();
        Enabled = enabled;
        Effect = effect;
        Order = order;
        Message = (message ?? string.Empty).Trim();
        Scope = (scope ?? new RuleScope()).Normalized();
        Match = m;
        UpdatedAt = now;
        UpdatedBy = actor;
    }
}

/// <summary>A quem a regra se aplica (listas vazias = todos).</summary>
public sealed class RuleScope
{
    public List<string> Roles { get; set; } = [];
    /// <summary>ExternalId (Guid) ou nome da pessoa (sem diferenciar maiúsculas).</summary>
    public List<string> Users { get; set; } = [];
    public List<string> Sources { get; set; } = [];
    /// <summary>Tipo do card (bug, user-story…); com a lista preenchida, só casa quando o tipo é conhecido.</summary>
    public List<string> CardTypes { get; set; } = [];
    public List<string> ExceptRoles { get; set; } = [];

    public RuleScope Normalized() => new()
    {
        Roles = Clean(Roles, lower: true),
        Users = Clean(Users, lower: true),
        Sources = Clean(Sources, lower: true).Where(GovernanceSource.IsValid).ToList(),
        CardTypes = Clean(CardTypes, lower: true),
        ExceptRoles = Clean(ExceptRoles, lower: true)
    };

    internal static List<string> Clean(IEnumerable<string>? values, bool lower = false) =>
        (values ?? [])
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => lower ? v.Trim().ToLowerInvariant() : v.Trim())
        .Distinct()
        .Take(100)
        .ToList();
}

/// <summary>
/// O que a regra casa. Cada dimensão preenchida precisa casar; regra sem nenhuma dimensão nunca casa
/// (evita barrar tudo por engano).
/// </summary>
public sealed class RuleMatch
{
    /// <summary>Glob sobre a ação ('POST Azure/card/*/actions/*', 'mcp:prmake_pr_*', 'hook:PreToolUse:Bash').</summary>
    public List<string> Actions { get; set; } = [];
    /// <summary>Glob sobre a ferramenta do Claude Code ('Bash', 'mcp__prmake__*', 'Write').</summary>
    public List<string> Tools { get; set; } = [];
    /// <summary>Regex (sem diferenciar maiúsculas) sobre o comando/entrada; basta uma casar.</summary>
    public List<string> Patterns { get; set; } = [];
    /// <summary>Regex que precisam casar todas, além de <see cref="Patterns"/>.</summary>
    public List<string> AllOf { get; set; } = [];
    /// <summary>Glob sobre o caminho de arquivo (Read/Write/Edit).</summary>
    public List<string> Paths { get; set; } = [];

    public bool IsEmpty => Actions.Count == 0 && Tools.Count == 0 && Patterns.Count == 0 && AllOf.Count == 0 && Paths.Count == 0;

    public RuleMatch Normalized() => new()
    {
        Actions = RuleScope.Clean(Actions),
        Tools = RuleScope.Clean(Tools),
        Patterns = RuleScope.Clean(Patterns),
        AllOf = RuleScope.Clean(AllOf),
        Paths = RuleScope.Clean(Paths)
    };

    public void Validate()
    {
        foreach (var p in Patterns.Concat(AllOf))
        {
            try
            {
                _ = new Regex(p, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException ex)
            {
                throw new DomainException($"Expressão regular inválida \"{p}\": {ex.Message}");
            }
        }
    }
}
