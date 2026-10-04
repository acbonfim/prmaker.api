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
    /// <summary>0045: consultas à Base Solvace e buscas no código na sessão (a base deve vir antes das buscas).</summary>
    public int? KbCalls { get; set; }
    public int? SearchCalls { get; set; }
    public DateTimeOffset? UsageUpdatedAt { get; set; }

    /// <summary>0047: o mesmo consumo separado por modelo (vazio = sessão antiga, só o total e <see cref="Model"/>).</summary>
    /// <remarks>Sessão gravada antes da 0047 não tem a chave no JSON e o EF materializa null — nunca devolve null.</remarks>
    public List<ExecutionModelUsage> Models { get => _models ??= []; set => _models = value ?? []; }
    private List<ExecutionModelUsage>? _models;

    /// <summary>0055: de onde a sessão leu (engenharia reversa × base × código) e os arquivos de código explorados.</summary>
    /// <remarks>Sessão anterior à 0055 não tem as chaves no JSON — nunca devolve null.</remarks>
    public List<ExecutionReadSource> Sources { get => _sources ??= []; set => _sources = value ?? []; }
    private List<ExecutionReadSource>? _sources;
    public List<ExecutionExploredFile> ExploredFiles { get => _explored ??= []; set => _explored = value ?? []; }
    private List<ExecutionExploredFile>? _explored;

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
    public int BaseKbCalls { get; set; }
    public int BaseSearchCalls { get; set; }
    /// <summary>0047: linha de base por modelo (o que a sessão já tinha, em cada modelo, no plano pai).</summary>
    public List<ExecutionModelUsage> BaseModels { get => _baseModels ??= []; set => _baseModels = value ?? []; }
    private List<ExecutionModelUsage>? _baseModels;
    /// <summary>0055: linha de base das leituras (o que a sessão já tinha lido no plano pai).</summary>
    public List<ExecutionReadSource> BaseSources { get => _baseSources ??= []; set => _baseSources = value ?? []; }
    private List<ExecutionReadSource>? _baseSources;
    public List<ExecutionExploredFile> BaseExploredFiles { get => _baseExplored ??= []; set => _baseExplored = value ?? []; }
    private List<ExecutionExploredFile>? _baseExplored;

    public int NetTurns() => Math.Max(0, Turns - BaseTurns);
    public long NetInputTokens() => Math.Max(0, InputTokens - BaseInputTokens);
    public long NetOutputTokens() => Math.Max(0, OutputTokens - BaseOutputTokens);
    public long NetCacheReadTokens() => Math.Max(0, CacheReadTokens - BaseCacheReadTokens);
    public long NetCacheWriteTokens() => Math.Max(0, CacheWriteTokens - BaseCacheWriteTokens);
    public int NetMcpCalls() => Math.Max(0, (McpCalls ?? 0) - BaseMcpCalls);
    public int NetScriptCalls() => Math.Max(0, (ScriptCalls ?? 0) - BaseScriptCalls);
    public int NetKbCalls() => Math.Max(0, (KbCalls ?? 0) - BaseKbCalls);
    public int NetSearchCalls() => Math.Max(0, (SearchCalls ?? 0) - BaseSearchCalls);

    /// <summary>0047: consumo líquido por modelo (sem a linha de base do mesmo modelo).</summary>
    public List<ExecutionModelUsage> NetModels() => Models
        .Select(m => m.Minus(BaseModels.FirstOrDefault(b => string.Equals(b.Model, m.Model, StringComparison.OrdinalIgnoreCase))))
        .Where(m => !m.IsEmpty)
        .ToList();

    /// <summary>0055: leituras líquidas por origem (sem a linha de base).</summary>
    public List<ExecutionReadSource> NetSources() => Sources
        .Select(s => s.Minus(BaseSources.FirstOrDefault(b => b.Key == s.Key)))
        .Where(s => !s.IsEmpty)
        .ToList();

    public List<ExecutionExploredFile> NetExploredFiles() => ExploredFiles
        .Select(f => f.Minus(BaseExploredFiles.FirstOrDefault(b => string.Equals(b.Path, f.Path, StringComparison.OrdinalIgnoreCase))))
        .Where(f => f.Reads > 0 || f.Tokens > 0)
        .ToList();
}
