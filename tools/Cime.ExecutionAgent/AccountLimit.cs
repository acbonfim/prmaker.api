using System.Globalization;
using System.Text.RegularExpressions;

namespace Cime.ExecutionAgent;

/// <summary>
/// Limite de uso da conta do Claude (0041): "You've hit your session limit · resets 7:50pm (America/Bahia)",
/// "usage limit reached… resets Oct 3, 9am", "rate limit". Devolve quando dá para tentar de novo — tentar antes só
/// queimaria as tentativas do pedido.
/// </summary>
public static partial class AccountLimit
{
    [GeneratedRegex(@"(session limit|usage limit|weekly limit|limit reached|hit your .{0,20}limit|rate[ _-]?limit|overloaded)", RegexOptions.IgnoreCase)]
    private static partial Regex LimitPattern();

    [GeneratedRegex(@"resets?\s+(?:at\s+)?(?:(?<mon>[A-Za-z]{3,9})\.?\s+(?<day>\d{1,2}),?\s+(?:at\s+)?)?(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?\s*(?:\((?<tz>[A-Za-z_]+(?:/[A-Za-z_+-]+)*)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex ResetPattern();

    /// <summary>null = não é limite da conta.</summary>
    public static DateTimeOffset? RetryAt(string? text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text) || !LimitPattern().IsMatch(text)) return null;
        var m = ResetPattern().Match(text);
        if (!m.Success)
            // Sobrecarga/limite sem horário: espera curta (rate limit) ou meia hora (limite de sessão).
            return now.AddMinutes(Regex.IsMatch(text, "session|usage|weekly", RegexOptions.IgnoreCase) ? 30 : 10);

        var tz = ResolveTimeZone(m.Groups["tz"].Value);
        var localNow = TimeZoneInfo.ConvertTime(now, tz);
        var hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        var ap = m.Groups["ap"].Value.ToLowerInvariant();
        if (ap == "pm" && hour < 12) hour += 12;
        if (ap == "am" && hour == 12) hour = 0;
        if (hour > 23 || minute > 59) return now.AddMinutes(30);

        var date = localNow.Date;
        var explicitDate = false;
        if (m.Groups["mon"].Success && m.Groups["day"].Success
            && DateTime.TryParseExact($"{m.Groups["mon"].Value[..3]} {m.Groups["day"].Value} {localNow.Year}", "MMM d yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            explicitDate = true;
            if (date < localNow.Date.AddDays(-1)) date = date.AddYears(1);
        }

        var local = date.AddHours(hour).AddMinutes(minute);
        var offset = tz.GetUtcOffset(local);
        var at = new DateTimeOffset(local, offset);
        if (!explicitDate && at <= now) at = at.AddDays(1);
        // Um minuto de folga depois do reset.
        at = at.AddMinutes(1);
        return at > now.AddDays(8) ? now.AddDays(1) : at.ToUniversalTime();
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }
}
