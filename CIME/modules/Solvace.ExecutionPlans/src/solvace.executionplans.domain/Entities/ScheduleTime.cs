using System.Globalization;
using System.Text.RegularExpressions;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0103: horário de agendamento de item/sub-tarefa. Entra como ISO-8601 (com fuso; sem fuso = horário de Brasília) ou
/// relativo a agora (<c>+2h30m</c>, <c>+90m</c>, <c>+1d</c>); fica em UTC (timestamptz só aceita offset 0) e aparece nos
/// textos em Brasília.
/// </summary>
public static partial class ScheduleTime
{
    /// <summary>Até quanto à frente dá para agendar.</summary>
    public static readonly TimeSpan MaxAhead = TimeSpan.FromDays(60);
    /// <summary>Folga para o relógio de quem pede (o "agora" da tela chega alguns segundos atrasado).</summary>
    public static readonly TimeSpan PastTolerance = TimeSpan.FromMinutes(2);

    /// <summary>Lê o horário (null/vazio = null). Inválido, no passado ou longe demais → <see cref="DomainException"/>.</summary>
    public static DateTimeOffset? Parse(string? value, DateTimeOffset now)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        DateTimeOffset at;
        if (Relative().Match(text.Replace(" ", string.Empty)) is { Success: true } m)
        {
            var days = Part(m, "d");
            var hours = Part(m, "h");
            var minutes = Part(m, "m");
            if (days + hours + minutes == 0)
                throw new DomainException($"Horário relativo sem tempo: '{text}' (ex.: +2h30m, +90m, +1d).");
            at = now + TimeSpan.FromDays(days) + TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        }
        else if (HasOffset(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withZone))
            at = withZone;
        else if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
                 || DateTime.TryParseExact(text, ["dd/MM/yyyy HH:mm", "dd/MM/yyyy H:mm", "dd/MM HH:mm"], CultureInfo.InvariantCulture,
                     DateTimeStyles.None, out local))
            at = FromBrasilia(local);
        else
            throw new DomainException($"Horário inválido: '{text}' — use ISO-8601 (2026-11-26T04:30:00-03:00; sem fuso = Brasília) ou relativo (+2h30m).");
        return Validate(at, now);
    }

    /// <summary>Confere o horário (no passado ou longe demais → erro) e devolve em UTC, sem segundos quebrados.</summary>
    public static DateTimeOffset Validate(DateTimeOffset at, DateTimeOffset now)
    {
        var utc = at.ToUniversalTime();
        utc = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
        if (utc < now - PastTolerance)
            throw new DomainException($"O horário {Format(utc)} já passou — escolha um horário futuro (ou execute agora).");
        if (utc > now + MaxAhead)
            throw new DomainException($"Dá para agendar até {MaxAhead.Days} dias à frente ({Format(now + MaxAhead)}).");
        return utc;
    }

    /// <summary>"26/11 04:30" no horário de Brasília (com o ano quando não é o deste ano).</summary>
    public static string Format(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, CardSituation.Brasilia);
        var thisYear = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, CardSituation.Brasilia).Year;
        return local.ToString(local.Year == thisYear ? "dd/MM HH:mm" : "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) + " (Brasília)";
    }

    private static DateTimeOffset FromBrasilia(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, CardSituation.Brasilia.GetUtcOffset(unspecified));
    }

    private static bool HasOffset(string text) =>
        text.EndsWith('Z') || text.EndsWith('z') || Offset().IsMatch(text);

    private static int Part(Match m, string group) =>
        m.Groups[group].Success && int.TryParse(m.Groups[group].Value, out var v) ? v : 0;

    [GeneratedRegex(@"^\+(?:(?<d>\d{1,2})d)?(?:(?<h>\d{1,4})h)?(?:(?<m>\d{1,5})(?:m|min)?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Relative();

    [GeneratedRegex(@"T.*[+-]\d{2}:?\d{2}$")]
    private static partial Regex Offset();
}
