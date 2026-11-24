namespace solvace.governance.domain;

/// <summary>0098: de onde veio a ação (tela, API, MCP, skill, executor, especialista, hook do Claude Code, sistema).</summary>
public static class GovernanceSource
{
    public const string Screen = "screen";
    public const string Api = "api";
    public const string Mcp = "mcp";
    public const string Skill = "skill";
    public const string Executor = "executor";
    public const string Specialist = "specialist";
    public const string Hook = "hook";
    public const string System = "system";

    public static readonly string[] All = [Screen, Api, Mcp, Skill, Executor, Specialist, Hook, System];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

/// <summary>0098: efeito de uma regra quando casa.</summary>
public static class GovernanceEffect
{
    public const string Monitor = "monitor";
    public const string Warn = "warn";
    public const string Block = "block";
    /// <summary>Por enquanto barra com a mensagem "precisa de aprovação" (aprovação de verdade fica para a rodada 2).</summary>
    public const string Approval = "approval";

    public static readonly string[] All = [Monitor, Warn, Block, Approval];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    public static int Weight(string effect) => effect switch
    {
        Block or Approval => 3,
        Warn => 2,
        Monitor => 1,
        _ => 0
    };
}

/// <summary>0098: modo global — off não avalia nem registra; monitor registra o que barraria; enforce aplica.</summary>
public static class GovernanceMode
{
    public const string Off = "off";
    public const string Monitor = "monitor";
    public const string Enforce = "enforce";

    public static readonly string[] All = [Off, Monitor, Enforce];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

public static class GovernanceDecisionKind
{
    public const string Allow = "allow";
    public const string Warn = "warn";
    public const string Block = "block";
}

public static class GovernanceOutcome
{
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Blocked = "blocked";
    public const string Warned = "warned";
    /// <summary>Casou regra, mas o modo (monitor) ou o efeito (monitor) só registra.</summary>
    public const string Monitored = "monitored";

    public static readonly string[] All = [Ok, Error, Blocked, Warned, Monitored];
}

/// <summary>0098: ações de um hook.</summary>
public static class GovernanceHookAction
{
    public const string Record = "record";
    public const string Evaluate = "evaluate";
    public const string Timeline = "timeline";
    public const string Webhook = "webhook";

    public static readonly string[] All = [Record, Evaluate, Timeline, Webhook];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

/// <summary>0098: eventos de hook — os do Claude Code (claude:*) e os gatilhos do servidor (server:&lt;glob da ação&gt;).</summary>
public static class GovernanceHookEvent
{
    public const string ClaudePrefix = "claude:";
    public const string ServerPrefix = "server:";

    public static readonly string[] Claude =
    [
        "claude:SessionStart", "claude:UserPromptSubmit", "claude:PreToolUse", "claude:PostToolUse", "claude:Stop",
        "claude:SubagentStop"
    ];

    public static bool IsValid(string? value) =>
        value is not null && (Claude.Contains(value) || (value.StartsWith(ServerPrefix) && value.Length > ServerPrefix.Length));
}
