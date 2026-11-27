using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0104: o Especialista do caso de um card escreve só rascunhos nos épicos do card; converter é da Mesa de gestão.</summary>
public class CardStoryAccess0104Tests
{
    private static EpicResponse Epic(bool allow = true, params (string Card, string Role)[] cards) => new()
    {
        Number = 1093,
        Key = "E1093",
        AllowCardStoryDrafts = allow,
        Cards = cards.Select(c => new EpicCardResponse { Card = c.Card, Role = c.Role }).ToList()
    };

    [Theory]
    [InlineData(EpicCardRole.Created)]
    [InlineData(EpicCardRole.Linked)]
    public void Card_created_or_linked_writes_drafts_when_allowed(string role)
    {
        var a = CardStoryAccess.Of(Epic(true, ("79000", EpicCardRole.Created), ("79047", role)), "#79047");
        Assert.True(a.InEpic);
        Assert.True(a.Writer);
        Assert.Null(a.WriteDenied);
        Assert.Contains("rascunhos", a.Note);
    }

    [Fact]
    public void Flag_off_blocks_writing_but_not_reading()
    {
        var a = CardStoryAccess.Of(Epic(false, ("79047", EpicCardRole.Linked)), "79047");
        Assert.True(a.InEpic);
        Assert.Contains("nao permite rascunhos", a.WriteDenied);
    }

    [Fact]
    public void Mentioned_card_reads_but_does_not_write()
    {
        var a = CardStoryAccess.Of(Epic(true, ("79047", EpicCardRole.Mentioned)), "79047");
        Assert.True(a.InEpic);
        Assert.False(a.Writer);
        Assert.Contains("so foi citado", a.WriteDenied);
    }

    [Fact]
    public void Card_outside_the_epic_is_not_in_it()
    {
        var a = CardStoryAccess.Of(Epic(true, ("79000", EpicCardRole.Created)), "79047");
        Assert.False(a.InEpic);
        Assert.False(a.Writer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("draft")]
    [InlineData(" Draft ")]
    public void Only_draft_status_from_the_card(string? status) => Assert.Null(CardStoryAccess.StatusDenied(status));

    [Theory]
    [InlineData("ready")]
    [InlineData("discarded")]
    public void Ready_or_discarded_refused_from_the_card(string status) =>
        Assert.Contains("rascunho", CardStoryAccess.StatusDenied(status));

    [Fact]
    public void Edits_only_own_drafts()
    {
        var own = new EpicStoryResponse { Number = 2001, SourceCard = "79047", Status = EpicStoryStatus.Draft };
        Assert.Null(CardStoryAccess.EditDenied(own, "79047"));
        Assert.Contains("nao foi criada pelo card", CardStoryAccess.EditDenied(own, "79100"));
        Assert.Contains("nao foi criada pelo card", CardStoryAccess.EditDenied(new EpicStoryResponse { Number = 2002, Status = EpicStoryStatus.Draft }, "79047"));
        Assert.Contains("nao e mais rascunho", CardStoryAccess.EditDenied(new EpicStoryResponse { Number = 2003, SourceCard = "79047", Status = EpicStoryStatus.Ready }, "79047"));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("management")]
    public void Desk_roles_convert(string role) => Assert.Null(CardStoryAccess.ConvertDenied([role, "user"], "Ana"));

    [Fact]
    public void Developer_does_not_convert()
    {
        var msg = CardStoryAccess.ConvertDenied(["user", "support"], "Alex Carlos");
        Assert.Contains("Mesa de gestao", msg);
        Assert.Contains("Alex Carlos", msg);
        Assert.NotNull(CardStoryAccess.ConvertDenied([], null));
    }
}
