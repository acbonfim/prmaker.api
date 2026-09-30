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
    public DateTimeOffset? UsageUpdatedAt { get; set; }
}
