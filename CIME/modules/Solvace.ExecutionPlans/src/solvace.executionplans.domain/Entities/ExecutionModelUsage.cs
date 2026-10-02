namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Consumo de uma sessão em UM modelo (0047): a análise roda no Opus e a correção no Sonnet na mesma sessão do Claude
/// Code — cada modelo é cobrado pelo seu preço. Valores acumulados do transcript (como os totais da sessão).
/// </summary>
public class ExecutionModelUsage
{
    public const int MaxModelLength = 100;

    public string Model { get; set; } = string.Empty;
    public int Turns { get; set; }
    /// <summary>Entrada nova (preço cheio).</summary>
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }

    public bool IsEmpty => Turns == 0 && InputTokens == 0 && OutputTokens == 0 && CacheReadTokens == 0 && CacheWriteTokens == 0;

    public ExecutionModelUsage Copy() => new()
    {
        Model = Model, Turns = Turns, InputTokens = InputTokens, OutputTokens = OutputTokens,
        CacheReadTokens = CacheReadTokens, CacheWriteTokens = CacheWriteTokens
    };

    /// <summary>Este menos a linha de base do mesmo modelo (nunca negativo).</summary>
    public ExecutionModelUsage Minus(ExecutionModelUsage? baseline) => baseline is null ? Copy() : new()
    {
        Model = Model,
        Turns = Math.Max(0, Turns - baseline.Turns),
        InputTokens = Math.Max(0, InputTokens - baseline.InputTokens),
        OutputTokens = Math.Max(0, OutputTokens - baseline.OutputTokens),
        CacheReadTokens = Math.Max(0, CacheReadTokens - baseline.CacheReadTokens),
        CacheWriteTokens = Math.Max(0, CacheWriteTokens - baseline.CacheWriteTokens)
    };

    /// <summary>Soma por modelo (sessões de um plano, planos de um relatório).</summary>
    public static List<ExecutionModelUsage> Sum(IEnumerable<ExecutionModelUsage> items) => items
        .GroupBy(m => m.Model, StringComparer.OrdinalIgnoreCase)
        .Select(g => new ExecutionModelUsage
        {
            Model = g.First().Model,
            Turns = g.Sum(m => m.Turns),
            InputTokens = g.Sum(m => m.InputTokens),
            OutputTokens = g.Sum(m => m.OutputTokens),
            CacheReadTokens = g.Sum(m => m.CacheReadTokens),
            CacheWriteTokens = g.Sum(m => m.CacheWriteTokens)
        })
        .Where(m => !m.IsEmpty)
        .OrderByDescending(m => m.InputTokens + m.OutputTokens + m.CacheReadTokens + m.CacheWriteTokens)
        .ToList();
}
