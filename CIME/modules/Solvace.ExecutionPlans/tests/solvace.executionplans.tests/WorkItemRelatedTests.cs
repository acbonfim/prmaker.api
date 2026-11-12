using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0094: item relatado ao card (menção/manual). A regra-chave: manual nunca é rebaixado por uma origem automática.</summary>
public class WorkItemRelatedTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T12:00:00Z");

    private static WorkItemRelated New(string source, long item = 136, long? task = null, string? context = null) =>
        new("78266", item, task, source, context, Guid.NewGuid(), "Alex", Now);

    [Fact]
    public void Creates_with_normalized_source_and_context()
    {
        var r = New(WorkItemRelatedSource.Specialist, 136, 137, "msg #12");
        Assert.Equal("78266", r.CardNumber);
        Assert.Equal(136, r.RelatedItemNumber);
        Assert.Equal(137, r.RelatedTaskNumber);
        Assert.Equal(WorkItemRelatedSource.Specialist, r.Source);
        Assert.Equal("msg #12", r.Context);
    }

    [Theory]
    [InlineData("SPECIALIST", "specialist")]
    [InlineData("Manual", "manual")]
    public void Source_is_lowercased(string given, string expected) =>
        Assert.Equal(expected, New(given).Source);

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    public void Rejects_unknown_source(string source) =>
        Assert.Throws<DomainException>(() => New(source));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_bad_item_number(long number) =>
        Assert.Throws<DomainException>(() => New(WorkItemRelatedSource.Manual, number));

    [Fact]
    public void Zero_or_negative_task_number_becomes_null() =>
        Assert.Null(New(WorkItemRelatedSource.Specialist, 136, 0).RelatedTaskNumber);

    [Fact]
    public void Touch_manual_is_never_downgraded_to_auto()
    {
        var r = New(WorkItemRelatedSource.Manual);
        var changed = r.Touch(WorkItemRelatedSource.Specialist, "msg nova", null);
        Assert.False(changed);
        Assert.Equal(WorkItemRelatedSource.Manual, r.Source);
    }

    [Fact]
    public void Touch_auto_can_change_to_manual()
    {
        var r = New(WorkItemRelatedSource.Specialist);
        var changed = r.Touch(WorkItemRelatedSource.Manual, null, null);
        Assert.True(changed);
        Assert.Equal(WorkItemRelatedSource.Manual, r.Source);
    }

    [Fact]
    public void Touch_auto_to_auto_updates_source_context_and_task()
    {
        var r = New(WorkItemRelatedSource.Plan, 136, null, "nota #3");
        var changed = r.Touch(WorkItemRelatedSource.Timeline, "Timeline", 137);
        Assert.True(changed);
        Assert.Equal(WorkItemRelatedSource.Timeline, r.Source);
        Assert.Equal("Timeline", r.Context);
        Assert.Equal(137, r.RelatedTaskNumber);
    }

    [Fact]
    public void Touch_without_changes_returns_false()
    {
        var r = New(WorkItemRelatedSource.Plan, 136, 137, "nota #3");
        Assert.False(r.Touch(WorkItemRelatedSource.Plan, "nota #3", 137));
    }

    [Fact]
    public void Touch_ignores_unknown_source()
    {
        var r = New(WorkItemRelatedSource.Plan);
        Assert.False(r.Touch("lixo", "x", null));
        Assert.Equal(WorkItemRelatedSource.Plan, r.Source);
    }

    [Fact]
    public void Touch_empty_context_does_not_clear_existing()
    {
        var r = New(WorkItemRelatedSource.Plan, 136, null, "nota #3");
        r.Touch(WorkItemRelatedSource.Timeline, "  ", null);
        Assert.Equal("nota #3", r.Context);
    }
}
