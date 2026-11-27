namespace solvace.azure.domain.Models;

/// <summary>Faixas do SLA (0105).</summary>
public static class CardSlaLevels
{
    public const string Grave = "grave";
    public const string Attention = "attention";
    public const string Ok = "ok";
}

/// <summary>
/// 0105: valores do filtro de SLA do board — grave/attention/ok (prazo correndo), stopped (prazo parado: cumprido ou
/// estourado) e none (card sem SLA).
/// </summary>
public static class CardSlaFilter
{
    public const string Stopped = "stopped";
    public const string None = "none";

    public static readonly IReadOnlyList<string> Values = [CardSlaLevels.Grave, CardSlaLevels.Attention, CardSlaLevels.Ok, Stopped, None];

    /// <summary>Valor do filtro em que o card cai.</summary>
    public static string KeyOf(CardSlaInfo? sla) => sla is null ? None : sla.Stopped ? Stopped : sla.Level;

    /// <summary>Valor conhecido (aceita os rótulos da tela: atenção, parado, sem SLA…); null = desconhecido.</summary>
    public static string? Normalize(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "grave" => CardSlaLevels.Grave,
            "attention" or "atenção" or "atencao" => CardSlaLevels.Attention,
            "ok" => CardSlaLevels.Ok,
            "stopped" or "parado" or "encerrado" or "cumprido" or "estourado" => Stopped,
            "none" or "sem sla" or "sem" => None,
            _ => null
        };
    }
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
