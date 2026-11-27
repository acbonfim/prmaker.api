using System.Text.Json;
using solvace.azure.application.Services;
using solvace.azure.domain.Models;
using solvace.azure.domain.Options;
using solvace.prform.PluginSettings;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0105: SLA dos cards — prazo em dias úteis por quem abriu × severidade, faixas grave/atenção/ok.</summary>
public class CardSla0105Tests
{
    // Quarta-feira, 2026-11-25, 12:00 em Brasília.
    private static readonly DateTimeOffset Wednesday = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, JsonElement> Fields(string severity, string? foundBy, DateTimeOffset created,
        DateTimeOffset? stateChange = null)
    {
        var f = new Dictionary<string, JsonElement>
        {
            ["Microsoft.VSTS.Common.Severity"] = JsonSerializer.SerializeToElement(severity),
            ["System.CreatedDate"] = JsonSerializer.SerializeToElement(created.ToString("O")),
        };
        if (foundBy is not null) f["Custom.Foundby"] = JsonSerializer.SerializeToElement(foundBy);
        if (stateChange is { } sc) f["Microsoft.VSTS.Common.StateChangeDate"] = JsonSerializer.SerializeToElement(sc.ToString("O"));
        return f;
    }

    private static CardSlaInfo? Sla(Dictionary<string, JsonElement> fields, DateTimeOffset now, string state = "New",
        CardSlaSettings? settings = null, string type = "Bug") =>
        CardSlaCalculator.Compute(fields, type, state, settings ?? CardSlaSettings.Default, now);

    [Fact]
    public void Default_rules_are_the_table_of_the_request()
    {
        var s = CardSlaSettings.Default;
        Assert.Equal(1, s.DaysFor("client", "1 - Critical"));
        Assert.Equal(2, s.DaysFor("client", "2 - High"));
        Assert.Equal(15, s.DaysFor("client", "3 - Medium"));
        Assert.Equal(20, s.DaysFor("client", "4 - Low"));
        Assert.Equal(2, s.DaysFor("internal", "Critical"));
        Assert.Equal(6, s.DaysFor("internal", "high"));
        Assert.Equal(30, s.DaysFor("internal", "3 - Medium"));
        Assert.Equal(40, s.DaysFor("internal", "4 - Low"));
        Assert.Equal(3, s.GraveMaxDays);
        Assert.Equal(15, s.AttentionMaxDays);
    }

    [Fact]
    public void Found_by_internal_team_is_internal_anything_else_is_client()
    {
        var created = Wednesday;
        Assert.Equal("internal", Sla(Fields("4 - Low", "Internal Team", created), Wednesday)!.Origin);
        Assert.Equal("internal", Sla(Fields("4 - Low", "internal team", created), Wednesday)!.Origin);
        Assert.Equal("client", Sla(Fields("4 - Low", "Gerdau", created), Wednesday)!.Origin);
        Assert.Equal("client", Sla(Fields("4 - Low", null, created), Wednesday)!.Origin); // vazio = cliente
    }

    [Fact]
    public void Deadline_skips_weekends_and_holidays()
    {
        var friday = new DateOnly(2026, 11, 27);
        Assert.Equal(new DateOnly(2026, 11, 30), CardSlaCalculator.AddBusinessDays(friday, 1, new HashSet<DateOnly>()));
        var saturday = new DateOnly(2026, 11, 28);
        Assert.Equal(new DateOnly(2026, 11, 30), CardSlaCalculator.AddBusinessDays(saturday, 1, new HashSet<DateOnly>()));
        Assert.Equal(new DateOnly(2026, 12, 1),
            CardSlaCalculator.AddBusinessDays(friday, 1, new HashSet<DateOnly> { new(2026, 11, 30) }));
    }

    [Fact]
    public void Client_medium_opened_today_is_attention_with_15_days()
    {
        var sla = Sla(Fields("3 - Medium", "Gerdau", Wednesday), Wednesday)!;
        Assert.Equal(15, sla.LimitDays);
        Assert.Equal(15, sla.RemainingDays);
        Assert.Equal(CardSlaLevels.Attention, sla.Level);
        Assert.Equal("15 d.u.", sla.Text);
        Assert.Equal("2026-12-16", sla.Deadline);
    }

    [Fact]
    public void Internal_low_opened_today_is_ok()
    {
        var sla = Sla(Fields("4 - Low", "Internal Team", Wednesday), Wednesday)!;
        Assert.Equal(40, sla.RemainingDays);
        Assert.Equal(CardSlaLevels.Ok, sla.Level);
    }

    [Fact]
    public void Client_critical_is_grave_and_overdue_after_the_deadline()
    {
        var opened = Wednesday.AddDays(-2); // segunda → prazo terça
        var sla = Sla(Fields("1 - Critical", "Mondelez", opened), Wednesday)!;
        Assert.Equal(-1, sla.RemainingDays);
        Assert.Equal(CardSlaLevels.Grave, sla.Level);
        Assert.Equal("Vencido há 1 d.u.", sla.Text);
        Assert.Equal(1, Sla(Fields("1 - Critical", "Mondelez", Wednesday), Wednesday)!.RemainingDays);

        var dueToday = Sla(Fields("1 - Critical", "Mondelez", Wednesday.AddDays(-1)), Wednesday)!;
        Assert.Equal(0, dueToday.RemainingDays);
        Assert.Equal("Vence hoje", dueToday.Text);
    }

    [Fact]
    public void Bands_follow_the_configured_thresholds()
    {
        var settings = CardSlaSettings.From(k => k switch
        {
            CardSlaKeys.GraveMaxDays => "3",
            CardSlaKeys.AttentionMaxDays => "10",
            _ => null
        });
        // Cliente Medium: 15 d.u.; aberto 5 d.u. atrás → faltam 10 (atenção); 12 atrás → faltam 3 (grave); 4 atrás → 11 (ok).
        Assert.Equal(CardSlaLevels.Attention, Sla(Fields("3 - Medium", "BAT", Wednesday.AddDays(-7)), Wednesday, settings: settings)!.Level);
        Assert.Equal(CardSlaLevels.Grave, Sla(Fields("3 - Medium", "BAT", Wednesday.AddDays(-16)), Wednesday, settings: settings)!.Level);
        Assert.Equal(CardSlaLevels.Ok, Sla(Fields("3 - Medium", "BAT", Wednesday.AddDays(-6)), Wednesday, settings: settings)!.Level);
    }

    [Fact]
    public void Stop_state_freezes_the_count_and_says_if_it_was_met()
    {
        var opened = Wednesday.AddDays(-9); // segunda 16/11; cliente High = 2 d.u. → prazo quarta 18/11
        var met = Sla(Fields("2 - High", "BAT", opened, stateChange: opened.AddDays(1)), Wednesday, state: "Closed")!;
        Assert.True(met.Stopped);
        Assert.True(met.Met);
        Assert.Equal(CardSlaLevels.Ok, met.Level);
        Assert.Equal("Cumprido", met.Text);

        var missed = Sla(Fields("2 - High", "BAT", opened, stateChange: opened.AddDays(7)), Wednesday, state: "Test in Production")!;
        Assert.False(missed.Met);
        Assert.Equal(CardSlaLevels.Grave, missed.Level);
        Assert.Equal("Estourado em 3 d.u.", missed.Text);
    }

    [Fact]
    public void No_sla_for_other_types_missing_severity_or_rule()
    {
        Assert.Null(Sla(Fields("2 - High", "BAT", Wednesday), Wednesday, type: "User Story"));
        Assert.Null(Sla(new Dictionary<string, JsonElement>(), Wednesday));
        var onlyClient = CardSlaSettings.From(k => k == CardSlaKeys.Rules ? """[{"origin":"client","severity":"Critical","days":1}]""" : null);
        Assert.Null(Sla(Fields("4 - Low", "BAT", Wednesday), Wednesday, settings: onlyClient));
        Assert.NotNull(Sla(Fields("1 - Critical", "BAT", Wednesday), Wednesday, settings: onlyClient));
        Assert.Null(Sla(Fields("1 - Critical", "BAT", Wednesday), Wednesday, settings: CardSlaSettings.From(k => k == CardSlaKeys.Enabled ? "false" : null)));
    }

    [Fact]
    public void Creation_date_from_hubspot_wins_over_created_date()
    {
        var f = Fields("3 - Medium", "BAT", Wednesday);
        f["Custom.CreationDate"] = JsonSerializer.SerializeToElement(Wednesday.AddDays(-7).ToString("O"));
        Assert.Equal(10, Sla(f, Wednesday)!.RemainingDays);
    }

    [Fact]
    public void Rules_typed_on_the_settings_screen_are_read_with_text_numbers()
    {
        var s = CardSlaSettings.From(k => k == CardSlaKeys.Rules ? """[{"origin":"internal","severity":"2 - High","days":"7"}]""" : null);
        Assert.Equal(7, s.DaysFor("internal", "2 - High"));
        // JSON quebrado = padrão
        Assert.Equal(1, CardSlaSettings.From(k => k == CardSlaKeys.Rules ? "[{" : null).DaysFor("client", "Critical"));
    }

    [Fact]
    public void Catalog_default_is_the_code_default()
    {
        var rules = PluginSettingsCatalog.Fields.Single(f => f.Key == CardSlaKeys.Rules);
        var parsed = CardSlaSettings.From(k => k == CardSlaKeys.Rules ? rules.Default : null);
        Assert.Equal(CardSlaSettings.DefaultRules, parsed.Rules);
        Assert.Equal("sla", rules.Section);
    }

    // ---------------------------------------------------------------- filtro do board

    private static DevOpsBoardItem Item(int id, CardSlaInfo? sla) => new() { Id = id, Type = "Bug", Title = $"Card {id}", State = "New", Sla = sla };

    private static readonly DevOpsBoardItem[] SlaItems =
    [
        Item(1, new CardSlaInfo { Level = CardSlaLevels.Grave }),
        Item(2, new CardSlaInfo { Level = CardSlaLevels.Attention }),
        Item(3, new CardSlaInfo { Level = CardSlaLevels.Ok }),
        Item(4, new CardSlaInfo { Level = CardSlaLevels.Grave, Stopped = true, Met = false }),
        Item(5, null)
    ];

    private static int[] Filter(params string[] sla) =>
        DevOpsDeskMapping.ApplyBoardFilters(SlaItems, new DevOpsBoardFilters { Sla = [.. sla] }, new DevOpsBoardConfig()).Select(i => i.Id).ToArray();

    [Fact]
    public void Board_filter_by_sla_band_in_or()
    {
        Assert.Equal([1], Filter("grave"));            // estourado com prazo parado não entra em grave
        Assert.Equal([1, 2], Filter("grave", "attention"));
        Assert.Equal([4], Filter("stopped"));
        Assert.Equal([5], Filter("none"));
        Assert.Equal([1, 2, 3, 4, 5], Filter());
    }

    [Fact]
    public void Board_filter_accepts_screen_labels_and_drops_unknown_values()
    {
        var f = DevOpsDeskMapping.NormalizeBoardFilters(new DevOpsBoardFilters { Sla = ["Atenção", "GRAVE", "grave", "xyz", "sem SLA"] });
        Assert.Equal(["attention", "grave", "none"], f.Sla);
        Assert.True(DevOpsDeskMapping.NormalizeBoardFilters(new DevOpsBoardFilters { Sla = ["xyz"] }).IsEmpty);
    }
}
