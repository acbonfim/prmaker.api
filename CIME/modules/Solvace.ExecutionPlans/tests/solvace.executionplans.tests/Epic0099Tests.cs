using solvace.executionplans.application;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0099: épico da Mesa de gestão — chave E{n}, dados, status, cards (papel só sobe) e número da sequência.</summary>
public class Epic0099Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T12:00:00Z");

    private static Epic New(string title = "Bugs do 5S", string? color = null, IEnumerable<string>? tags = null) =>
        new(title, "Organizar os bugs do 5S", color, tags, "Time Revamp", "Stories", null, Guid.NewGuid(), "Alex", Now);

    // ── EpicKey ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("E123", 123)]
    [InlineData("e7", 7)]
    [InlineData(" #E1234 ", 1234)]
    public void Key_parses(string key, long expected)
    {
        Assert.True(EpicKey.TryParse(key, out var n));
        Assert.Equal(expected, n);
        Assert.True(EpicKey.IsEpic(key));
        Assert.Equal($"E{expected}", EpicKey.Normalize(key));
    }

    [Theory]
    [InlineData("78123")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("E")]
    [InlineData("E0")]
    [InlineData("E12a")]
    [InlineData("EX12")]
    [InlineData("AB#78123")]
    public void Key_rejects_cards_and_garbage(string? key)
    {
        Assert.False(EpicKey.TryParse(key, out _));
        Assert.False(EpicKey.IsEpic(key));
        Assert.Null(EpicKey.Normalize(key));
    }

    [Fact]
    public void Key_of_number() => Assert.Equal("E42", EpicKey.Of(42));

    // ── dados ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Creates_active_with_default_color_and_clean_tags()
    {
        var e = New(tags: ["Kaizen", " kaizen ", "#5S", ""]);
        Assert.Equal(EpicStatus.Active, e.Status);
        Assert.Equal(Epic.DefaultColor, e.Color);
        Assert.Equal(["Kaizen", "5S"], e.Tags);
        Assert.Equal("Time Revamp", e.DevOpsTeam);
        Assert.Null(e.ClosedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_is_required(string title) => Assert.Throws<DomainException>(() => New(title));

    [Theory]
    [InlineData("#ABCDEF", "#abcdef")]
    [InlineData("7a5cff", "#7a5cff")]
    [InlineData("#fff", "#fff")]
    public void Color_is_normalized(string given, string expected) => Assert.Equal(expected, New(color: given).Color);

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    public void Color_rejects_invalid(string color) => Assert.Throws<DomainException>(() => New(color: color));

    [Fact]
    public void Status_closes_and_reopens()
    {
        var e = New();
        Assert.True(e.SetStatus("DONE", Now.AddHours(1)));
        Assert.Equal(EpicStatus.Done, e.Status);
        Assert.Equal(Now.AddHours(1), e.ClosedAt);
        Assert.False(e.SetStatus("done", Now.AddHours(2)));
        Assert.True(e.SetStatus("archived", Now.AddHours(3)));
        Assert.Equal(Now.AddHours(1), e.ClosedAt); // continua fechado desde a primeira vez
        Assert.True(e.SetStatus("active", Now.AddHours(4)));
        Assert.Null(e.ClosedAt);
        Assert.Throws<DomainException>(() => e.SetStatus("nope", Now));
    }

    // ── cards ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Card_role_only_goes_up()
    {
        var e = New();
        var (card, changed) = e.AddCard("AB#78123", EpicCardRole.Mentioned, null, null, null, "Alex", Now);
        Assert.True(changed);
        Assert.Equal("78123", card.CardNumber);
        Assert.Equal(EpicCardRole.Mentioned, card.Role);

        Assert.True(e.AddCard("#78123", null, "Erro ao salvar", "Bug", null, "Alex", Now).Changed); // linked + foto
        Assert.Equal(EpicCardRole.Linked, card.Role);
        Assert.Equal("Erro ao salvar", card.Title);

        Assert.True(e.AddCard("78123", EpicCardRole.Created, null, null, null, "Alex", Now).Changed);
        Assert.False(e.AddCard("78123", EpicCardRole.Mentioned, null, null, null, "Alex", Now).Changed);
        Assert.False(e.AddCard("78123", EpicCardRole.Linked, null, null, null, "Alex", Now).Changed);
        Assert.Equal(EpicCardRole.Created, card.Role);
        Assert.Single(e.Cards);
    }

    [Theory]
    [InlineData("E12")]
    [InlineData("abc")]
    [InlineData("")]
    public void Card_must_be_a_devops_number(string card) =>
        Assert.Throws<DomainException>(() => New().AddCard(card, null, null, null, null, "Alex", Now));

    [Fact]
    public void Remove_card_is_idempotent()
    {
        var e = New();
        e.AddCard("78123", null, null, null, null, "Alex", Now);
        Assert.NotNull(e.RemoveCard("78123", Now));
        Assert.Null(e.RemoveCard("78123", Now));
        Assert.Empty(e.Cards);
    }

    // ── número (sequência global) ────────────────────────────────────────────────────────────

    [Fact]
    public void Number_is_assigned_once_and_drives_the_key()
    {
        var e = New();
        Assert.Equal(0, e.Number);
        e.AssignNumber(1234);
        Assert.Equal(1234, e.Number);
        Assert.Equal("E1234", e.Key);
        Assert.Throws<InvalidOperationException>(() => e.AssignNumber(1235));
        Assert.Throws<ArgumentOutOfRangeException>(() => New().AssignNumber(0));
    }

    [Fact]
    public void Summary_carries_key_and_counts()
    {
        var e = New();
        e.AssignNumber(77);
        var s = EpicSummary.From(e, new EpicCounts { Cards = 2, Items = 5, OpenItems = 3 });
        Assert.Equal("E77", s.Key);
        Assert.Equal(5, s.Counts.Items);
        Assert.Equal(3, s.Counts.OpenItems);
    }

    // ── filtro de status da lista ────────────────────────────────────────────────────────────

    [Fact]
    public void Status_filter_defaults_to_not_archived()
    {
        Assert.Equal([EpicStatus.Active, EpicStatus.Paused, EpicStatus.Done], EpicApplication.ParseStatuses(null)!);
        Assert.Null(EpicApplication.ParseStatuses("all"));
        Assert.Equal([EpicStatus.Active, EpicStatus.Archived], EpicApplication.ParseStatuses("active, ARCHIVED")!);
        Assert.Throws<DomainException>(() => EpicApplication.ParseStatuses("nope"));
    }
}
