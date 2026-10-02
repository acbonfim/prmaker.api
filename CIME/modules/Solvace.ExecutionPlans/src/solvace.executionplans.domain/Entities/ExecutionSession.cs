namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Sessão do Claude Code que trabalhou no plano (0033): <c>CLAUDE_CODE_SESSION_ID</c>, máquina e pasta — para
/// retomar exatamente aquela conversa (<c>claude --resume</c>) — e o custo dela em tokens (lido do transcript pela skill).
/// </summary>
public class ExecutionSession
{
    public const int MaxSessionIdLength = 100;
    public const int MaxHostLength = 200;
    public const int MaxCwdLength = 500;

    public string SessionId { get; set; } = string.Empty;
    public string? Host { get; set; }
    public string? Cwd { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    public int Turns { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public string? Model { get; set; }

    /// <summary>Chamadas ao PRMake pelo MCP (<c>mcp__prmake__*</c>) e pelo script (<c>prmake-plan.sh</c>) na sessão (0041) — medição.</summary>
    public int? McpCalls { get; set; }
    public int? ScriptCalls { get; set; }
    public DateTimeOffset? UsageUpdatedAt { get; set; }

    /// <summary>
    /// 0044: a mesma sessão do Claude continua da análise para a correção (outro plano) e o transcript é acumulado — o
    /// que ela já tinha gasto no plano pai fica aqui e é descontado (o plano mostra só o consumo dele).
    /// </summary>
    public bool BaselineSet { get; set; }
    public int BaseTurns { get; set; }
    public long BaseInputTokens { get; set; }
    public long BaseOutputTokens { get; set; }
    public long BaseCacheReadTokens { get; set; }
    public long BaseCacheWriteTokens { get; set; }
    public int BaseMcpCalls { get; set; }
    public int BaseScriptCalls { get; set; }

    public int NetTurns() => Math.Max(0, Turns - BaseTurns);
    public long NetInputTokens() => Math.Max(0, InputTokens - BaseInputTokens);
    public long NetOutputTokens() => Math.Max(0, OutputTokens - BaseOutputTokens);
    public long NetCacheReadTokens() => Math.Max(0, CacheReadTokens - BaseCacheReadTokens);
    public long NetCacheWriteTokens() => Math.Max(0, CacheWriteTokens - BaseCacheWriteTokens);
    public int NetMcpCalls() => Math.Max(0, (McpCalls ?? 0) - BaseMcpCalls);
    public int NetScriptCalls() => Math.Max(0, (ScriptCalls ?? 0) - BaseScriptCalls);
}
