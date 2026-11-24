using System.Text.RegularExpressions;

namespace solvace.governance.domain.Entities;

/// <summary>
/// 0101: padrão de banco de dados ou de estrutura de aplicação (convenção que a análise e a correção seguem ao escrever
/// SQL, migrações e código). Diferente da <see cref="GovernanceRule"/> (guardrail de ação). Mesma escala de efeitos da
/// governança; começa sempre em monitorar. Itens importados guardam a origem (wiki + versão, engenharia reversa).
/// </summary>
public class GovernanceStandard
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Area { get; private set; } = StandardArea.Database;
    public string Topic { get; private set; } = "general";
    public string Title { get; private set; } = string.Empty;
    public string TextPt { get; private set; } = string.Empty;
    public string? TextEn { get; private set; }
    public StandardExamples Examples { get; private set; } = new();
    public StandardScope AppliesTo { get; private set; } = new();
    public StandardSource Source { get; private set; } = new();
    public string Status { get; private set; } = StandardStatus.Proposed;
    public string Effect { get; private set; } = GovernanceEffect.Monitor;
    public StandardCheck Check { get; private set; } = new();
    public int Order { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    protected GovernanceStandard() { }

    public GovernanceStandard(Guid id, string key)
    {
        Id = id;
        Key = GovernanceRule.NormalizeKey(key);
    }

    public void Update(StandardData d, string actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(d.Title)) throw new DomainException("Informe o título do padrão.");
        if (string.IsNullOrWhiteSpace(d.TextPt)) throw new DomainException("Informe o texto do padrão.");
        if (!StandardArea.IsValid(d.Area)) throw new DomainException($"Área inválida: {d.Area}.");
        if (!StandardStatus.IsValid(d.Status)) throw new DomainException($"Estado inválido: {d.Status}.");
        if (!GovernanceEffect.IsValid(d.Effect) || d.Effect == GovernanceEffect.Approval)
            throw new DomainException($"Efeito inválido para padrão: {d.Effect}.");
        var check = (d.Check ?? new StandardCheck()).Normalized();
        check.Validate();
        Code = Clean(d.Code, 20) ?? Key.ToUpperInvariant();
        Area = d.Area;
        Topic = Clean(d.Topic, 40)?.ToLowerInvariant() ?? "general";
        Title = Clean(d.Title, 300)!;
        TextPt = d.TextPt.Trim();
        TextEn = string.IsNullOrWhiteSpace(d.TextEn) ? null : d.TextEn.Trim();
        Examples = (d.Examples ?? new StandardExamples()).Normalized();
        AppliesTo = (d.AppliesTo ?? new StandardScope()).Normalized();
        Source = d.Source ?? new StandardSource();
        Status = d.Status;
        Effect = d.Effect;
        Check = check;
        Order = d.Order;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    /// <summary>Efeito que vale de fato: proposto/descontinuado só monitora.</summary>
    public string EffectiveEffect => Status == StandardStatus.Active ? Effect : GovernanceEffect.Monitor;

    private static string? Clean(string? v, int max) =>
        string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length <= max ? v.Trim() : v.Trim()[..max];
}

public sealed record StandardData
{
    public string? Code { get; init; }
    public string Area { get; init; } = StandardArea.Database;
    public string? Topic { get; init; }
    public string Title { get; init; } = string.Empty;
    public string TextPt { get; init; } = string.Empty;
    public string? TextEn { get; init; }
    public StandardExamples? Examples { get; init; }
    public StandardScope? AppliesTo { get; init; }
    public StandardSource? Source { get; init; }
    public string Status { get; init; } = StandardStatus.Proposed;
    public string Effect { get; init; } = GovernanceEffect.Monitor;
    public StandardCheck? Check { get; init; }
    public int Order { get; init; }
}

public static class StandardArea
{
    public const string Database = "database";
    public const string Application = "application";
    public static bool IsValid(string? v) => v is Database or Application;
}

public static class StandardStatus
{
    /// <summary>Proposta (ex.: itens (NEW) da wiki que o DBA ainda valida): só monitora.</summary>
    public const string Proposed = "proposed";
    public const string Active = "active";
    public const string Deprecated = "deprecated";
    public static bool IsValid(string? v) => v is Proposed or Active or Deprecated;
}

public sealed class StandardExamples
{
    public List<string> Good { get; set; } = [];
    public List<string> Bad { get; set; } = [];

    public StandardExamples Normalized() => new() { Good = RuleScope.Clean(Good), Bad = RuleScope.Clean(Bad) };
}

/// <summary>A que se aplica (listas vazias = tudo). Objetos: new | migrated | legacy.</summary>
public sealed class StandardScope
{
    public List<string> Platforms { get; set; } = [];
    public List<string> Repositories { get; set; } = [];
    public List<string> Modules { get; set; } = [];
    public List<string> Objects { get; set; } = [];

    public StandardScope Normalized() => new()
    {
        Platforms = RuleScope.Clean(Platforms, lower: true),
        Repositories = RuleScope.Clean(Repositories, lower: true),
        Modules = RuleScope.Clean(Modules, lower: true),
        Objects = RuleScope.Clean(Objects, lower: true)
    };

    public bool AppliesTo(string? repository, IReadOnlyCollection<string>? modules) =>
        (Repositories.Count == 0 || (repository is not null && Repositories.Any(r => GovernanceRuleGlob(r, repository))))
        && (Modules.Count == 0 || (modules is not null && modules.Any(m => Modules.Contains(m.ToLowerInvariant()))));

    private static bool GovernanceRuleGlob(string glob, string value) => Engine.GovernanceRuleEngine.Glob(glob, value);
}

/// <summary>De onde o padrão veio: wiki (ref = ID da regra na página), engenharia reversa (ref = módulo#ITEM) ou manual.</summary>
public sealed class StandardSource
{
    public string Kind { get; set; } = "manual";
    public string Ref { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Url { get; set; }
    /// <summary>Hash do texto importado — a reimportação compara para dizer "mudou".</summary>
    public string? Hash { get; set; }
}

/// <summary>
/// Conferência automática opcional: regex sobre o texto gravado (sql | migration | code), glob de caminho (path) ou a
/// consulta de validação da wiki (sql, rodada sob demanda no banco de um ambiente, somente leitura).
/// <see cref="MustMatch"/> = o texto/caminho precisa casar (senão é achado); falso = casar é o achado.
/// </summary>
public sealed class StandardCheck
{
    public const string None = "none";
    public const string Regex = "regex";
    public const string Path = "path";
    public const string SqlKind = "sql";
    /// <summary>Conferência embutida do código (ex.: nome de FK fora do padrão) — <see cref="Pattern"/> = nome dela.</summary>
    public const string Builtin = "builtin";

    public string Kind { get; set; } = None;
    public string? Target { get; set; }
    public string? Pattern { get; set; }
    public bool MustMatch { get; set; }
    public string? Sql { get; set; }
    public string? Hint { get; set; }

    public StandardCheck Normalized() => new()
    {
        Kind = string.IsNullOrWhiteSpace(Kind) ? None : Kind.Trim().ToLowerInvariant(),
        Target = string.IsNullOrWhiteSpace(Target) ? null : Target.Trim().ToLowerInvariant(),
        Pattern = string.IsNullOrWhiteSpace(Pattern) ? null : Pattern.Trim(),
        MustMatch = MustMatch,
        Sql = string.IsNullOrWhiteSpace(Sql) ? null : Sql.Trim(),
        Hint = string.IsNullOrWhiteSpace(Hint) ? null : Hint.Trim()
    };

    public void Validate()
    {
        if (Kind is not (None or Regex or Path or SqlKind or Builtin)) throw new DomainException($"Conferência inválida: {Kind}.");
        if (Kind is Regex or Path or Builtin && string.IsNullOrWhiteSpace(Pattern))
            throw new DomainException("A conferência precisa de um padrão.");
        if (Kind == Regex)
        {
            try { _ = new System.Text.RegularExpressions.Regex(Pattern!, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException ex) { throw new DomainException($"Expressão regular inválida \"{Pattern}\": {ex.Message}"); }
        }
        if (Kind == SqlKind && string.IsNullOrWhiteSpace(Sql)) throw new DomainException("A conferência por SQL precisa da consulta.");
    }
}
