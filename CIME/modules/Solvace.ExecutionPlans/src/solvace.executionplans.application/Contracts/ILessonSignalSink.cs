namespace solvace.executionplans.application.Contracts;

/// <summary>
/// 0106: sinais de erro que o PRMake percebe sozinho (item do especialista cancelado logo depois, entrega refeita, item
/// reaberto para refazer, pedido de mudanças num PR) — implementado no host (lições da governança). Best-effort: nunca
/// derruba a ação que gerou o sinal. Mesmo <c>key</c> = mesmo acontecimento, um sinal só.
/// </summary>
public interface ILessonSignalSink
{
    Task SignalAsync(string card, string kind, string key, string text, string? reference, CancellationToken ct);
}

/// <summary>Tipos de sinal (os mesmos da governança).</summary>
public static class LessonSignals
{
    public const string ItemCancelled = "item-cancelled";
    public const string TaskRedo = "task-redo";
    public const string ItemRedo = "item-redo";
    public const string PrChangesRequested = "pr-changes-requested";

    /// <summary>Item do especialista cancelado até este tempo depois de criado = sinal de que não era o que a pessoa queria.</summary>
    public static readonly TimeSpan CancelledSoon = TimeSpan.FromMinutes(30);
}
