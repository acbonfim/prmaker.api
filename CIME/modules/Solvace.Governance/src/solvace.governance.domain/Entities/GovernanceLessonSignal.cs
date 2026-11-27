namespace solvace.governance.domain.Entities;

/// <summary>
/// 0106: sinal de erro que o PRMake registra sozinho (sem depender do agente admitir): item do especialista cancelado logo
/// depois de criado, entrega refeita, item reaberto para refazer, pedido de mudanças num PR do card. O próximo agente que
/// entra no card vê o sinal nas regras/contexto e o transforma em lição (<see cref="GovernanceLesson"/>) ou o descarta
/// dizendo por quê. Um por <see cref="Key"/> (o mesmo acontecimento não vira dois sinais).
/// </summary>
public class GovernanceLessonSignal
{
    public const int MaxText = 1000;

    public long Id { get; private set; }
    public string Card { get; private set; } = string.Empty;
    public string Kind { get; private set; } = LessonSignalKind.Other;
    public string Key { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string? Ref { get; private set; }
    public string Status { get; private set; } = LessonSignalStatus.Open;
    public long? LessonId { get; private set; }
    public string? Resolution { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }

    public string Code => $"S{Id}";

    protected GovernanceLessonSignal() { }

    public GovernanceLessonSignal(string card, string kind, string key, string text, string? reference, DateTimeOffset now)
    {
        Card = Cut(card, 20) ?? throw new DomainException("Informe o card do sinal.");
        Kind = LessonSignalKind.IsValid(kind) ? kind : LessonSignalKind.Other;
        Key = Cut(key, 200) ?? throw new DomainException("Informe a chave do sinal.");
        Text = Cut(text, MaxText) ?? throw new DomainException("Informe o que aconteceu.");
        Ref = Cut(reference, 200);
        CreatedAt = now;
    }

    public void Learned(long lessonId, string actor, DateTimeOffset now)
    {
        Status = LessonSignalStatus.Learned;
        LessonId = lessonId;
        Resolution = null;
        ResolvedAt = now;
        ResolvedBy = Cut(actor, 200);
    }

    public void Dismiss(string? reason, string actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Diga por que o sinal não é um erro a aprender.");
        Status = LessonSignalStatus.Dismissed;
        Resolution = Cut(reason, 500);
        ResolvedAt = now;
        ResolvedBy = Cut(actor, 200);
    }

    private static string? Cut(string? v, int max) =>
        string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length <= max ? v.Trim() : v.Trim()[..max];
}

public static class LessonSignalKind
{
    /// <summary>Item criado pelo especialista e cancelado por uma pessoa logo depois.</summary>
    public const string ItemCancelled = "item-cancelled";
    /// <summary>Entrega de uma sub-tarefa concluída refeita (script recusado, texto reescrito).</summary>
    public const string TaskRedo = "task-redo";
    /// <summary>Item reaberto para refazer sub-tarefas.</summary>
    public const string ItemRedo = "item-redo";
    /// <summary>Pedido de mudanças (changes requested) num PR do card.</summary>
    public const string PrChangesRequested = "pr-changes-requested";
    public const string Other = "other";

    public static readonly string[] All = [ItemCancelled, TaskRedo, ItemRedo, PrChangesRequested, Other];
    public static bool IsValid(string? v) => v is not null && All.Contains(v);
}

public static class LessonSignalStatus
{
    public const string Open = "open";
    public const string Learned = "learned";
    public const string Dismissed = "dismissed";
}
