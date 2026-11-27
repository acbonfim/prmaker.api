using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using solvace.azure.domain.Models;
using solvace.azure.domain.Options;

namespace solvace.azure.application.Services;

/// <summary>Uma linha da tabela de SLA: quem abriu (client | internal) × severidade → prazo máximo em dias úteis.</summary>
public sealed record CardSlaRule(string Origin, string Severity, int Days);

/// <summary>
/// 0105: regras do SLA lidas das chaves <see cref="CardSlaKeys"/> do plugin "AzureDevOps Configurations". Chave ausente ou
/// inválida = o padrão daqui (o mesmo do catálogo das Configurações).
/// </summary>
public sealed partial class CardSlaSettings
{
    public const string Client = "client";
    public const string Internal = "internal";

    public static readonly IReadOnlyList<CardSlaRule> DefaultRules =
    [
        new(Client, "1 - Critical", 1), new(Client, "2 - High", 2), new(Client, "3 - Medium", 15), new(Client, "4 - Low", 20),
        new(Internal, "1 - Critical", 2), new(Internal, "2 - High", 6), new(Internal, "3 - Medium", 30), new(Internal, "4 - Low", 40)
    ];

    public static readonly string DefaultRulesJson = JsonSerializer.Serialize(
        DefaultRules.Select(r => new { origin = r.Origin, severity = r.Severity, days = r.Days }));

    public const int DefaultGraveMaxDays = 3;
    public const int DefaultAttentionMaxDays = 15;
    public const string DefaultWorkItemTypes = "Bug";
    public const string DefaultOriginField = "Custom.Foundby";
    public const string DefaultInternalValues = "Internal Team";
    public const string DefaultSeverityField = "Microsoft.VSTS.Common.Severity";
    public const string DefaultStartFields = "Custom.CreationDate,System.CreatedDate";
    public const string DefaultStopStates = "Test in Production,Closed";
    public const string DefaultTimeZone = "America/Sao_Paulo";
    /// <summary>Data em que o card entrou no estado atual (fim do prazo quando para).</summary>
    public const string StateChangeDateField = "Microsoft.VSTS.Common.StateChangeDate";

    public static CardSlaSettings Default { get; } = From(_ => null);

    public bool Enabled { get; init; } = true;
    public IReadOnlyList<CardSlaRule> Rules { get; init; } = DefaultRules;
    public int GraveMaxDays { get; init; } = DefaultGraveMaxDays;
    public int AttentionMaxDays { get; init; } = DefaultAttentionMaxDays;
    public IReadOnlyList<string> WorkItemTypes { get; init; } = [];
    public string OriginField { get; init; } = DefaultOriginField;
    public IReadOnlyList<string> InternalValues { get; init; } = [];
    public string SeverityField { get; init; } = DefaultSeverityField;
    public IReadOnlyList<string> StartFields { get; init; } = [];
    public IReadOnlyList<string> StopStates { get; init; } = [];
    public IReadOnlySet<DateOnly> Holidays { get; init; } = new HashSet<DateOnly>();
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    /// <summary>Lê as chaves pelo <paramref name="value"/> (valor do plugin; null = ausente).</summary>
    public static CardSlaSettings From(Func<string, string?> value)
    {
        string? Get(string key) => value(key) is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        var grave = Int(Get(CardSlaKeys.GraveMaxDays)) ?? DefaultGraveMaxDays;
        return new CardSlaSettings
        {
            Enabled = !bool.TryParse(Get(CardSlaKeys.Enabled), out var on) || on,
            Rules = ParseRules(Get(CardSlaKeys.Rules)) ?? DefaultRules,
            GraveMaxDays = grave,
            AttentionMaxDays = Math.Max(grave, Int(Get(CardSlaKeys.AttentionMaxDays)) ?? DefaultAttentionMaxDays),
            WorkItemTypes = List(Get(CardSlaKeys.WorkItemTypes) ?? DefaultWorkItemTypes),
            OriginField = Get(CardSlaKeys.OriginField) ?? DefaultOriginField,
            InternalValues = List(Get(CardSlaKeys.InternalValues) ?? DefaultInternalValues),
            SeverityField = Get(CardSlaKeys.SeverityField) ?? DefaultSeverityField,
            StartFields = List(Get(CardSlaKeys.StartFields) ?? DefaultStartFields),
            StopStates = List(Get(CardSlaKeys.StopStates) ?? DefaultStopStates),
            Holidays = List(Get(CardSlaKeys.Holidays) ?? string.Empty)
                .Select(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : (DateOnly?)null)
                .OfType<DateOnly>().ToHashSet(),
            TimeZone = Zone(Get(CardSlaKeys.TimeZone) ?? DefaultTimeZone)
        };
    }

    /// <summary>Campos que o cálculo lê do card (o board pede só estes além dos dele).</summary>
    public IEnumerable<string> Fields =>
        new[] { OriginField, SeverityField, StateChangeDateField }.Concat(StartFields).Where(f => f.Length > 0);

    public bool AppliesTo(string type) =>
        Enabled && WorkItemTypes.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>Prazo da origem × severidade ("Critical" casa com "1 - Critical"); null = sem regra.</summary>
    public int? DaysFor(string origin, string severity) =>
        Rules.FirstOrDefault(r => string.Equals(r.Origin, origin, StringComparison.OrdinalIgnoreCase)
                                  && SeverityKey(r.Severity) == SeverityKey(severity))?.Days;

    public static string SeverityKey(string severity) => SeverityPrefix().Replace(severity.Trim(), string.Empty).ToLowerInvariant();

    [GeneratedRegex(@"^\d+\s*-\s*")]
    private static partial Regex SeverityPrefix();

    private static IReadOnlyList<CardSlaRule>? ParseRules(string? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var rules = new List<CardSlaRule>();
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                var origin = Text(row, "origin");
                var severity = Text(row, "severity");
                var days = row.TryGetProperty("days", out var d)
                    ? d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var n) ? n : Int(d.ValueKind == JsonValueKind.String ? d.GetString() : null)
                    : null;
                if (origin is { Length: > 0 } && severity is { Length: > 0 } && days is >= 0)
                    rules.Add(new CardSlaRule(origin, severity, days.Value));
            }
            return rules;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;

    private static int? Int(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;

    /// <summary>Lista em csv (a, b) ou JSON (["a","b"]).</summary>
    private static IReadOnlyList<string> List(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('['))
            try
            {
                return JsonSerializer.Deserialize<List<string>>(raw)?.Select(s => s.Trim()).Where(s => s.Length > 0).ToList() ?? [];
            }
            catch (JsonException)
            {
                // cai no csv
            }
        return raw.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Imagem sem a base de fusos: horário de Brasília (sem horário de verão desde 2019).
            return TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(-3), id, id);
        }
    }
}

/// <summary>
/// 0105: cálculo do SLA de um card. Prazo = data de abertura + N dias úteis (sem sábado, domingo e feriados, no fuso do SLA);
/// o que falta conta os dias úteis de hoje (exclusive) até o prazo — 0 = vence hoje, negativo = vencido. Card num estado de
/// entrega para de contar na data em que entrou nele.
/// </summary>
public static class CardSlaCalculator
{
    public static CardSlaInfo? Compute(IReadOnlyDictionary<string, JsonElement> fields, string type, string state,
        CardSlaSettings settings, DateTimeOffset now)
    {
        if (!settings.AppliesTo(type)) return null;
        var severity = DevOpsDeskMapping.FieldText(fields, settings.SeverityField);
        if (string.IsNullOrWhiteSpace(severity)) return null;

        var originValue = DevOpsDeskMapping.FieldText(fields, settings.OriginField)?.Trim();
        // Origem vazia conta como cliente (o prazo mais curto).
        var origin = originValue is { Length: > 0 } && settings.InternalValues.Any(v => string.Equals(v, originValue, StringComparison.OrdinalIgnoreCase))
            ? CardSlaSettings.Internal
            : CardSlaSettings.Client;
        if (settings.DaysFor(origin, severity) is not { } limit) return null;

        var start = settings.StartFields.Select(f => Date(fields, f)).FirstOrDefault(d => d is not null);
        if (start is not { } opened) return null;

        var stopped = settings.StopStates.Any(s => string.Equals(s, state, StringComparison.OrdinalIgnoreCase));
        var stoppedAt = stopped ? Date(fields, CardSlaSettings.StateChangeDateField) ?? now : (DateTimeOffset?)null;
        var end = stoppedAt ?? now;

        var startDay = LocalDay(opened, settings.TimeZone);
        var endDay = LocalDay(end, settings.TimeZone);
        var deadline = AddBusinessDays(startDay, limit, settings.Holidays);
        var remaining = endDay <= deadline
            ? BusinessDaysBetween(endDay, deadline, settings.Holidays)
            : -BusinessDaysBetween(deadline, endDay, settings.Holidays);
        var elapsed = endDay <= startDay ? 0 : BusinessDaysBetween(startDay, endDay, settings.Holidays);
        bool? met = stopped ? remaining >= 0 : null;

        return new CardSlaInfo
        {
            Origin = origin,
            OriginValue = originValue,
            Severity = severity.Trim(),
            LimitDays = limit,
            Start = opened,
            Deadline = deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ElapsedDays = elapsed,
            RemainingDays = remaining,
            Level = stopped
                ? met == true ? CardSlaLevels.Ok : CardSlaLevels.Grave
                : remaining <= settings.GraveMaxDays ? CardSlaLevels.Grave
                : remaining <= settings.AttentionMaxDays ? CardSlaLevels.Attention
                : CardSlaLevels.Ok,
            Stopped = stopped,
            StoppedAt = stoppedAt,
            Met = met,
            Text = stopped
                ? met == true ? "Cumprido" : $"Estourado em {-remaining} d.u."
                : remaining switch
                {
                    0 => "Vence hoje",
                    < 0 => $"Vencido há {-remaining} d.u.",
                    _ => $"{remaining} d.u."
                }
        };
    }

    public static bool IsBusinessDay(DateOnly day, IReadOnlySet<DateOnly> holidays) =>
        day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(day);

    /// <summary>Dia de <paramref name="start"/> + <paramref name="days"/> dias úteis (aberto no sábado com 1 d.u. = segunda).</summary>
    public static DateOnly AddBusinessDays(DateOnly start, int days, IReadOnlySet<DateOnly> holidays)
    {
        var day = start;
        for (var added = 0; added < days;)
        {
            day = day.AddDays(1);
            if (IsBusinessDay(day, holidays)) added++;
        }
        return day;
    }

    /// <summary>Dias úteis em (<paramref name="from"/>, <paramref name="to"/>] — <paramref name="from"/> &lt;= <paramref name="to"/>.</summary>
    public static int BusinessDaysBetween(DateOnly from, DateOnly to, IReadOnlySet<DateOnly> holidays)
    {
        var count = 0;
        for (var day = from.AddDays(1); day <= to; day = day.AddDays(1))
            if (IsBusinessDay(day, holidays)) count++;
        return count;
    }

    private static DateOnly LocalDay(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    private static DateTimeOffset? Date(IReadOnlyDictionary<string, JsonElement> fields, string field) =>
        DevOpsDeskMapping.FieldText(fields, field) is { Length: > 0 } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;
}
