using solvace.prform.domain.Entities;
using solvace.prform.domain.Promotion;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0074: ciclo de vida do item da promoção (fila → executor → PR) e do lote.</summary>
public class PromotionItemTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-30T12:00:00Z");
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Worker = Guid.NewGuid();

    private static PromotionItem Item(string target = "release-3.274.10", string mode = PromotionMode.CherryPick) =>
        new("77317", "Botão sobreposto", "Solvace-Org/edv-solvace-apps", "hotfix/77317", "master", "release-{version}", target, false, mode, T0);

    [Fact]
    public void New_item_is_queued_with_normalized_mode()
    {
        var i = Item(mode: "DIRECT");
        Assert.Equal(PromotionItemStatus.Queued, i.Status);
        Assert.Equal(PromotionMode.Direct, i.Mode);
        Assert.True(i.IsActive);
    }

    [Fact]
    public void Item_requires_card_repository_branch_and_target()
    {
        Assert.Throws<DomainException>(() => new PromotionItem(" ", "t", "r", "b", null, "x", "x", false, null, T0));
        Assert.Throws<DomainException>(() => new PromotionItem("1", "t", "", "b", null, "x", "x", false, null, T0));
        Assert.Throws<DomainException>(() => new PromotionItem("1", "t", "r", "", null, "x", "x", false, null, T0));
        Assert.Throws<DomainException>(() => new PromotionItem("1", "t", "r", "b", null, "x", "", false, null, T0));
    }

    [Fact]
    public void Claim_runs_with_a_lease_and_only_once()
    {
        var i = Item();
        i.Claim(Worker, T0);

        Assert.Equal(PromotionItemStatus.Running, i.Status);
        Assert.Equal(1, i.Attempts);
        Assert.Equal(T0 + PromotionItem.LeaseDuration, i.LeaseUntil);
        Assert.Throws<DomainException>(() => i.Claim(Guid.NewGuid(), T0));
    }

    [Fact]
    public void Heartbeat_extends_the_lease_only_for_the_owner_worker()
    {
        var i = Item();
        i.Claim(Worker, T0);
        i.Heartbeat(Worker, T0.AddMinutes(3));
        Assert.Equal(T0.AddMinutes(3) + PromotionItem.LeaseDuration, i.LeaseUntil);
        Assert.Throws<DomainException>(() => i.Heartbeat(Guid.NewGuid(), T0.AddMinutes(4)));
    }

    [Fact]
    public void Expired_lease_returns_to_the_queue_then_fails_after_the_max_attempts()
    {
        var i = Item();
        i.Claim(Worker, T0);
        Assert.False(i.ExpireLease(T0.AddMinutes(1)));                    // trava ainda vale

        Assert.True(i.ExpireLease(T0 + PromotionItem.LeaseDuration + TimeSpan.FromSeconds(1)));
        Assert.Equal(PromotionItemStatus.Queued, i.Status);
        Assert.Null(i.WorkerId);

        var later = T0.AddHours(1);
        for (var n = i.Attempts; n < PromotionItem.MaxAttempts; n++)
        {
            i.Claim(Worker, later);
            Assert.True(i.ExpireLease(later + PromotionItem.LeaseDuration + TimeSpan.FromSeconds(1)));
        }
        Assert.Equal(PromotionItemStatus.Error, i.Status);
        Assert.NotNull(i.FinishedAt);
    }

    [Fact]
    public void Opened_records_the_pull_request_and_already_existing_is_flagged()
    {
        var a = Item();
        a.Claim(Worker, T0);
        a.MarkOpened(321, "https://github.com/x/y/pull/321", 9, alreadyExisted: false, T0.AddMinutes(1));
        Assert.Equal(PromotionItemStatus.Opened, a.Status);
        Assert.Equal(321, a.PullRequestNumber);
        Assert.Null(a.WorkerId);

        var b = Item("master");
        b.Claim(Worker, T0);
        b.MarkOpened(322, "https://github.com/x/y/pull/322", null, alreadyExisted: true, T0.AddMinutes(1));
        Assert.Equal(PromotionItemStatus.AlreadyOpen, b.Status);
    }

    [Fact]
    public void Conflict_keeps_the_files_one_per_line_and_can_be_retried()
    {
        var i = Item();
        i.Claim(Worker, T0);
        i.RecordCommits(2, "hotfix/77317-release-3.274.10");
        i.MarkConflict([" src/a.ts ", "", "src/b.ts"], null, T0.AddMinutes(1));

        Assert.Equal(PromotionItemStatus.Conflict, i.Status);
        Assert.Equal("src/a.ts\nsrc/b.ts", i.ConflictFiles);
        Assert.True(i.IsRetryable);

        i.Retry(T0.AddMinutes(5));
        Assert.Equal(PromotionItemStatus.Queued, i.Status);
        Assert.Null(i.ConflictFiles);
        Assert.Null(i.NewBranch);
        Assert.Equal(0, i.Attempts);
    }

    [Fact]
    public void Only_failed_items_are_retryable_and_only_queued_ones_cancel()
    {
        var opened = Item();
        opened.Claim(Worker, T0);
        opened.MarkOpened(1, "u", null, false, T0);
        Assert.False(opened.IsRetryable);
        Assert.Throws<DomainException>(() => opened.Retry(T0));
        Assert.Throws<DomainException>(() => opened.Cancel(T0));

        var queued = Item();
        queued.Cancel(T0);
        Assert.Equal(PromotionItemStatus.Cancelled, queued.Status);
        Assert.True(queued.IsRetryable);

        Assert.Throws<DomainException>(() => queued.Finish(PromotionItemStatus.Running, null, T0));
    }

    [Fact]
    public void Batch_requires_a_valid_version_and_owner_and_tracks_when_it_finishes()
    {
        Assert.Throws<DomainException>(() => new PromotionBatch("abc", null, Owner, "Alex", T0));
        Assert.Throws<DomainException>(() => new PromotionBatch("3.274.10", null, Guid.Empty, "Alex", T0));

        var batch = new PromotionBatch("V3.274.10", "HOTFIX", Owner, "Alex Carlos", T0);
        Assert.Equal("3.274.10", batch.Version);
        Assert.Equal("hotfix", batch.Kind);

        var a = batch.AddItem(Item());
        var b = batch.AddItem(Item("master"));
        Assert.Equal([1, 2], new[] { a.Position, b.Position });
        Assert.True(batch.IsActive);

        a.Claim(Worker, T0);
        a.MarkOpened(1, "u", null, false, T0.AddMinutes(1));
        batch.Refresh(T0.AddMinutes(1));
        Assert.Null(batch.FinishedAt);

        b.Claim(Worker, T0);
        b.Finish(PromotionItemStatus.NoChanges, "já está no destino", T0.AddMinutes(2));
        batch.Refresh(T0.AddMinutes(2));
        Assert.Equal(T0.AddMinutes(2), batch.FinishedAt);
        Assert.False(batch.IsActive);
    }

    [Fact]
    public void Batch_has_a_maximum_number_of_items()
    {
        var batch = new PromotionBatch("3.274.10", null, Owner, "Alex", T0);
        for (var n = 0; n < PromotionBatch.MaxItems; n++)
            batch.AddItem(Item());
        Assert.Throws<DomainException>(() => batch.AddItem(Item()));
    }
}
