namespace solvace.azure.domain.Models;

/// <summary>Faixas do SLA (0105).</summary>
public static class CardSlaLevels
{
    public const string Grave = "grave";
    public const string Attention = "attention";
    public const string Ok = "ok";
}

/// <summary>
/// 0105: SLA de um card — prazo máximo em dias úteis pela origem (cliente × time interno) e severidade, contado da abertura.
/// <c>RemainingDays</c> negativo = vencido. Com <c>Stopped</c> (card num estado de entrega), o prazo parou em <c>StoppedAt</c>
/// e <c>Met</c> diz se foi cumprido.
/// </summary>
public sealed class CardSlaInfo
{
    /// <summary>client | internal.</summary>
    public string Origin { get; set; } = string.Empty;
    /// <summary>Valor do campo de origem (ex.: Gerdau, Internal Team).</summary>
    public string? OriginValue { get; set; }
    public string Severity { get; set; } = string.Empty;
    public int LimitDays { get; set; }
    public DateTimeOffset Start { get; set; }
    /// <summary>Último dia útil do prazo (yyyy-MM-dd, no fuso do SLA).</summary>
    public string Deadline { get; set; } = string.Empty;
    public int ElapsedDays { get; set; }
    public int RemainingDays { get; set; }
    /// <summary>grave | attention | ok.</summary>
    public string Level { get; set; } = CardSlaLevels.Ok;
    public bool Stopped { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public bool? Met { get; set; }
    /// <summary>Texto curto pronto (ex.: "3 d.u.", "Vence hoje", "Vencido há 2 d.u.", "Cumprido").</summary>
    public string Text { get; set; } = string.Empty;
}
