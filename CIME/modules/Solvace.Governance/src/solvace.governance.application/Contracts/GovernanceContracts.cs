using solvace.governance.domain.Engine;
using solvace.governance.domain.Entities;

namespace solvace.governance.application.Contracts;

public interface IGovernanceRepository
{
    Task<List<GovernanceRule>> GetRulesAsync(CancellationToken ct = default);
    Task<GovernanceRule?> GetRuleAsync(Guid id, CancellationToken ct = default);
    Task<bool> RuleKeyExistsAsync(string key, CancellationToken ct = default);
    void AddRule(GovernanceRule rule);
    void RemoveRule(GovernanceRule rule);

    Task<List<GovernanceHook>> GetHooksAsync(CancellationToken ct = default);
    Task<GovernanceHook?> GetHookAsync(Guid id, CancellationToken ct = default);
    Task<bool> HookKeyExistsAsync(string key, CancellationToken ct = default);
    void AddHook(GovernanceHook hook);
    void RemoveHook(GovernanceHook hook);

    /// <summary>Configurações (cria a linha padrão em memória quando ainda não existe; grava no SaveChanges).</summary>
    Task<GovernanceSettings> GetSettingsAsync(CancellationToken ct = default);

    // 0101: padrões e rascunhos de importação.
    Task<List<GovernanceStandard>> GetStandardsAsync(CancellationToken ct = default);
    Task<GovernanceStandard?> GetStandardAsync(Guid id, CancellationToken ct = default);
    void AddStandard(GovernanceStandard standard);
    void RemoveStandard(GovernanceStandard standard);
    Task<GovernanceStandardDraft?> GetDraftAsync(Guid id, CancellationToken ct = default);
    void AddDraft(GovernanceStandardDraft draft);

    // 0106: lições aprendidas e sinais de erro.
    Task<List<GovernanceLesson>> GetLessonsAsync(CancellationToken ct = default);
    Task<GovernanceLesson?> GetLessonAsync(long id, CancellationToken ct = default);
    void AddLesson(GovernanceLesson lesson);
    Task<List<GovernanceLessonSignal>> GetSignalsAsync(string? card, string? status, int take, CancellationToken ct = default);
    Task<GovernanceLessonSignal?> GetSignalAsync(long id, CancellationToken ct = default);
    Task<bool> SignalKeyExistsAsync(string key, CancellationToken ct = default);
    void AddSignal(GovernanceLessonSignal signal);

    void AddEvents(IEnumerable<GovernanceEvent> events);
    Task<(int Total, List<GovernanceEvent> Items)> QueryEventsAsync(GovernanceEventFilter filter, CancellationToken ct = default);
    Task<int> PurgeEventsAsync(DateTimeOffset before, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Fotografia das regras/hooks/configurações em memória (o motor não vai ao banco por requisição).</summary>
public sealed record GovernancePolicy(
    IReadOnlyList<GovernanceRule> Rules,
    IReadOnlyList<GovernanceHook> Hooks,
    GovernanceSettings Settings,
    string Version)
{
    /// <summary>0101: padrões de banco/aplicação (sem os descontinuados).</summary>
    public IReadOnlyList<GovernanceStandard> Standards { get; init; } = [];
}

public interface IGovernancePolicyProvider
{
    Task<GovernancePolicy> GetAsync(CancellationToken ct = default);
    /// <summary>Esquece a fotografia desta instância (depois de gravar regra/hook/configuração).</summary>
    void Invalidate();
}

/// <summary>Fila da trilha: grava fora da requisição, em lote (nunca lança).</summary>
public interface IGovernanceEventSink
{
    void Enqueue(GovernanceEventData data);
}

/// <summary>Completa o lote antes de gravar (ex.: card e fase a partir do plano ou do item) — implementado no host.</summary>
public interface IGovernanceEventEnricher
{
    Task<IReadOnlyList<GovernanceEventData>> EnrichAsync(IReadOnlyList<GovernanceEventData> batch, CancellationToken ct);
}

/// <summary>Gatilhos do servidor (server:*): executados pelo sink depois de gravar o lote.</summary>
public interface IGovernanceServerHookRunner
{
    Task RunAsync(GovernanceHook hook, GovernanceEvent evt, CancellationToken ct);
}

public sealed record GovernanceEventFilter
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public Guid? UserId { get; init; }
    public string? User { get; init; }
    public int? Card { get; init; }
    public string? Source { get; init; }
    public string? Category { get; init; }
    public string? Outcome { get; init; }
    public string? Action { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int Size { get; init; } = 50;
}

public sealed record GovernanceActor(Guid? UserId, string Name, IReadOnlyCollection<string> Roles, string Source = "screen");

public sealed record RuleRequest
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public bool Enabled { get; init; } = true;
    public string Effect { get; init; } = "monitor";
    public int Order { get; init; } = 500;
    public string? Message { get; init; }
    public RuleScope? Scope { get; init; }
    public RuleMatch? Match { get; init; }
}

public sealed record HookRequest
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Event { get; init; } = string.Empty;
    public string? Matcher { get; init; }
    public string Action { get; init; } = "record";
    public Dictionary<string, string>? Config { get; init; }
    public bool Enabled { get; init; } = true;
    public int Order { get; init; } = 500;
}

public sealed record SettingsRequest
{
    public string Mode { get; init; } = "monitor";
    public int RetentionDays { get; init; } = 365;
    public bool HookFailOpen { get; init; } = true;
    public int HookTimeoutMs { get; init; } = 1_500;
    public int HookCacheSeconds { get; init; } = 60;
    public List<string>? ExemptRoles { get; init; }
    public List<string>? RecordSources { get; init; }
    public List<Milestone>? Milestones { get; init; }
}

public sealed record EvaluateRequest
{
    public string Source { get; init; } = "hook";
    public string? Event { get; init; }
    public string? Tool { get; init; }
    public string? Input { get; init; }
    public string? Path { get; init; }
    public string? Action { get; init; }
    public int? CardNumber { get; init; }
    public string? CardType { get; init; }
    public string? SessionId { get; init; }
    public string? Machine { get; init; }
    public string? Cwd { get; init; }
}

/// <summary>Simulador: a entrada no mesmo formato do evaluate (plano) + rascunho opcional de regra e papéis simulados.</summary>
public sealed record RuleTestRequest
{
    public string Source { get; init; } = "hook";
    public string? Event { get; init; }
    public string? Tool { get; init; }
    public string? Input { get; init; }
    public string? Path { get; init; }
    public string? Action { get; init; }
    public int? CardNumber { get; init; }
    public string? CardType { get; init; }
    /// <summary>Rascunho de regra: testa só ela (ativa, modo enforce) em vez das regras gravadas.</summary>
    public RuleRequest? Rule { get; init; }
    public List<string>? Roles { get; init; }

    public EvaluateRequest ToEvaluate() => new()
    {
        Source = Source, Event = Event, Tool = Tool, Input = Input, Path = Path, Action = Action, CardNumber = CardNumber, CardType = CardType
    };
}

public sealed record EvaluateResponse(string Decision, string Mode, string? Reason, IReadOnlyList<GovernanceRuleHit> Rules, string WouldDecide)
{
    public static EvaluateResponse From(GovernanceDecision d) => new(d.Decision, d.Mode, d.Reason, d.Rules, d.WouldDecide);
}

public sealed record HookEventRequest
{
    public string Event { get; init; } = string.Empty;
    /// <summary>hook | executor | specialist (o script sabe se roda no executor/especialista).</summary>
    public string? Source { get; init; }
    public string? Tool { get; init; }
    public string? Input { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public int? DurationMs { get; init; }
    public string? Outcome { get; init; }
    public int? CardNumber { get; init; }
    public string? SessionId { get; init; }
    public string? Machine { get; init; }
    public string? Detail { get; init; }
    public List<string>? Rules { get; init; }
}

public sealed record HooksConfigResponse(
    string Mode,
    bool FailOpen,
    int TimeoutMs,
    int CacheSeconds,
    IReadOnlyList<HookConfigItem> Hooks,
    IReadOnlyList<string> ToolMatchers,
    string Version);

public sealed record HookConfigItem(string Key, string Event, string Matcher, string Action, Dictionary<string, string> Config);
