using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0104: flag do épico "rascunhos de histórias pelos cards" e o card de origem da história.</summary>
public class CardStoryDrafts0104Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-27T12:00:00Z");

    private static Epic New() => new("Board do tech lead", null, null, null, null, null, null, Guid.NewGuid(), "Alex", Now);

    [Fact]
    public void Flag_is_on_by_default_and_toggles()
    {
        var e = New();
        Assert.True(e.AllowCardStoryDrafts);
        Assert.False(e.SetAllowCardStoryDrafts(true, Now));
        Assert.True(e.SetAllowCardStoryDrafts(false, Now.AddMinutes(1)));
        Assert.False(e.AllowCardStoryDrafts);
        Assert.Equal(Now.AddMinutes(1), e.UpdatedAt);
        Assert.True(e.SetAllowCardStoryDrafts(true, Now.AddMinutes(2)));
        Assert.True(EpicSummary.From(e, null).AllowCardStoryDrafts);
    }

    [Fact]
    public void Story_keeps_the_source_card()
    {
        var s = new EpicStory(Guid.NewGuid(), 1093, "user-story", "Trocar de site não reabre a sala", null, null, null, null, null, null, null,
            null, null, "Especialista", Now, " #79047 ");
        Assert.Equal("79047", s.SourceCard);
        Assert.Equal(EpicStoryStatus.Draft, s.Status);
        Assert.Equal("79047", EpicStoryResponse.From(s, Now).SourceCard);
        Assert.Null(new EpicStory(Guid.NewGuid(), 1093, null, "Pela Mesa", null, null, null, null, null, null, null, null, null, "Ana", Now).SourceCard);
    }

    [Fact]
    public void Source_card_never_comes_from_the_rest_body()
    {
        var req = System.Text.Json.JsonSerializer.Deserialize<SaveEpicStoryRequest>("""{"title":"x","sourceCard":"123"}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Null(req!.SourceCard);
    }
}
