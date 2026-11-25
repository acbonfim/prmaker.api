using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0085 r4: tipos próprios do item de trabalho (o tipo do card no DevOps não é o tipo do item).</summary>
public class WorkItemTypeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-05T12:00:00Z");

    [Theory]
    [InlineData(null, "task")]
    [InlineData("", "task")]
    [InlineData("Pull Request", "pull-request")]
    [InlineData("review", "review")]
    [InlineData("bug", "bug")]          // legado: itens antigos continuam como estão
    [InlineData("User Story", "user-story")]
    [InlineData("us", "user-story")]
    public void Normalize_keeps_the_slug_and_the_legacy_types(string? type, string expected) =>
        Assert.Equal(expected, WorkItemType.Normalize(type));

    [Theory]
    [InlineData("tipo com/barra")]
    [InlineData("um-tipo-com-mais-de-vinte")]
    public void Normalize_rejects_what_is_not_a_slug(string type) =>
        Assert.Throws<DomainException>(() => WorkItemType.Normalize(type));

    [Theory]
    [InlineData("bug", "fix")]
    [InlineData("user-story", "implementation")]
    [InlineData("US", "implementation")]
    [InlineData("review", "review")]
    [InlineData(null, null)]
    public void New_items_map_the_legacy_types(string? type, string? expected) =>
        Assert.Equal(expected, WorkItemType.MapLegacy(type));

    [Fact]
    public void Correction_type_follows_the_card_type()
    {
        Assert.Equal("fix", WorkItemType.CorrectionFor(userStory: false));
        Assert.Equal("implementation", WorkItemType.CorrectionFor(userStory: true));
    }

    [Fact]
    public void Duplicate_of_a_legacy_item_gets_the_new_type()
    {
        var old = new WorkItem("77434", WorkItemType.UserStory, "Antigo", null, WorkItemOrigin.User, null, null,
            [new WorkTaskInput { Title = "a", Kind = WorkTaskKind.Manual }], null, "Bruno", Now);
        Assert.Equal("user-story", old.Type);
        Assert.Equal("implementation", old.Duplicate(null, "Bruno", Now).Type);
        var review = new WorkItem("77434", WorkItemType.Review, "Revisar", null, WorkItemOrigin.User, null, null, [], null, "Bruno", Now);
        Assert.Equal("review", review.Duplicate(null, "Bruno", Now).Type);
    }
}
