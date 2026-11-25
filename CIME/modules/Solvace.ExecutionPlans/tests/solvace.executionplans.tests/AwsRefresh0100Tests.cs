using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// 0100: fila de coleta da AWS — só o executor de quem pediu pega o pedido (regra 78723), trava de 5 min, expiração da
/// fila em 30 min e o acompanhamento do deploy em andamento.
/// </summary>
public class AwsRefresh0100Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T19:00:00Z");
    private static readonly Guid Alex = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bia = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AlexMac = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BiaMac = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static AwsRefresh New(Guid owner, string card = "78720", DateTimeOffset? at = null, DateTimeOffset? notBefore = null,
        string source = AwsRefreshSource.Button, int follow = 0) =>
        new(card, owner, owner == Alex ? "Alex Carlos" : "Bia", source, follow, notBefore, at ?? Now);

    [Fact]
    public void Executor_only_gets_the_requests_of_its_owner()
    {
        var bias = New(Bia, at: Now.AddMinutes(-10));
        var alexs = New(Alex, at: Now.AddMinutes(-1));

        Assert.Same(alexs, AwsRefreshQueue.PickNext([bias, alexs], Alex, Now));
        Assert.Same(bias, AwsRefreshQueue.PickNext([bias, alexs], Bia, Now));
        // Só o da Bia na fila: o executor do Alex não pega (nunca cai no executor de outra pessoa).
        Assert.Null(AwsRefreshQueue.PickNext([bias], Alex, Now));
    }

    [Fact]
    public void Claim_by_another_person_is_refused()
    {
        var bias = New(Bia);
        Assert.Throws<DomainException>(() => bias.Claim(Alex, AlexMac, Now));
        Assert.Equal(AwsRefreshStatus.Queued, bias.Status);
    }

    [Fact]
    public void Claim_takes_a_five_minute_lease_and_counts_the_attempt()
    {
        var r = New(Alex);
        r.Claim(Alex, AlexMac, Now);
        Assert.Equal(AwsRefreshStatus.Claimed, r.Status);
        Assert.Equal(AlexMac, r.WorkerId);
        Assert.Equal(Now.AddMinutes(5), r.LeaseUntil);
        Assert.Equal(1, r.Attempt);
        Assert.Throws<DomainException>(() => r.Claim(Alex, AlexMac, Now)); // já está com um executor
    }

    [Fact]
    public void Oldest_ready_first_and_one_collection_per_card_at_a_time()
    {
        var older = New(Alex, "1", at: Now.AddMinutes(-5));
        var newer = New(Alex, "2", at: Now.AddMinutes(-1));
        Assert.Same(older, AwsRefreshQueue.PickNext([newer, older], Alex, Now));

        var running = New(Alex, "1", at: Now.AddMinutes(-9));
        running.Claim(Alex, AlexMac, Now.AddMinutes(-1));
        Assert.Same(newer, AwsRefreshQueue.PickNext([running, older, newer], Alex, Now));
    }

    [Fact]
    public void Follow_waits_for_not_before()
    {
        var follow = New(Alex, notBefore: Now.AddMinutes(2), source: AwsRefreshSource.Follow, follow: 1);
        Assert.False(follow.IsReady(Now));
        Assert.Null(AwsRefreshQueue.PickNext([follow], Alex, Now));
        Assert.Same(follow, AwsRefreshQueue.PickNext([follow], Alex, Now.AddMinutes(2)));
    }

    [Fact]
    public void Expedite_brings_a_waiting_follow_forward()
    {
        var follow = New(Alex, notBefore: Now.AddMinutes(2), source: AwsRefreshSource.Follow, follow: 1);
        Assert.True(follow.Expedite(Now));
        Assert.True(follow.IsReady(Now));
        Assert.False(follow.Expedite(Now));
    }

    [Fact]
    public void Lease_expires_when_the_executor_goes_silent()
    {
        var r = New(Alex);
        r.Claim(Alex, AlexMac, Now);
        Assert.False(r.ExpireIfStale(Now.AddMinutes(4)));
        Assert.True(r.Heartbeat(AlexMac, Now.AddMinutes(4)));
        Assert.False(r.ExpireIfStale(Now.AddMinutes(8)));     // a trava foi estendida até 9 min
        Assert.False(r.Heartbeat(BiaMac, Now.AddMinutes(8))); // outro executor não estende
        Assert.True(r.ExpireIfStale(Now.AddMinutes(10)));
        Assert.Equal(AwsRefreshStatus.Expired, r.Status);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Queue_expires_after_thirty_minutes_counting_from_not_before()
    {
        var r = New(Alex);
        Assert.False(r.ExpireIfStale(Now.AddMinutes(30)));
        Assert.True(r.ExpireIfStale(Now.AddMinutes(31)));
        Assert.Equal(AwsRefreshStatus.Expired, r.Status);

        var follow = New(Alex, notBefore: Now.AddMinutes(10), source: AwsRefreshSource.Follow, follow: 1);
        Assert.False(follow.ExpireIfStale(Now.AddMinutes(35)));
        Assert.True(follow.ExpireIfStale(Now.AddMinutes(41)));
    }

    [Fact]
    public void Report_from_the_same_executor_counts_even_after_the_lease()
    {
        var r = New(Alex);
        r.Claim(Alex, AlexMac, Now);
        r.ExpireIfStale(Now.AddMinutes(6));
        Assert.Throws<DomainException>(() => r.Report(Alex, BiaMac, "done", null, 1000, Now.AddMinutes(7)));
        Assert.Throws<DomainException>(() => r.Report(Bia, AlexMac, "done", null, 1000, Now.AddMinutes(7)));
        r.Report(Alex, AlexMac, "done", null, 420_000, Now.AddMinutes(7));
        Assert.Equal(AwsRefreshStatus.Done, r.Status);
        Assert.Null(r.Error);
        Assert.Equal(420_000, r.DurationMs);
    }

    [Fact]
    public void Report_failed_keeps_the_error_and_rejects_unknown_status()
    {
        var r = New(Alex);
        r.Claim(Alex, AlexMac, Now);
        Assert.Throws<DomainException>(() => r.Report(Alex, AlexMac, "ok", null, null, Now));
        r.Report(Alex, AlexMac, "failed", "aws: command not found", 50, Now.AddSeconds(5));
        Assert.Equal(AwsRefreshStatus.Failed, r.Status);
        Assert.Equal("aws: command not found", r.Error);
    }

    [Fact]
    public void Snapshot_with_the_request_id_completes_it()
    {
        var r = New(Alex);
        r.Claim(Alex, AlexMac, Now);
        Assert.True(r.CompleteBySnapshot(8123, Now.AddSeconds(9)));
        Assert.Equal(AwsRefreshStatus.Done, r.Status);
        Assert.False(r.CompleteBySnapshot(1, Now.AddSeconds(10)));
        // O report do executor depois do PUT não quebra (já concluído).
        r.Report(Alex, AlexMac, "done", null, 9000, Now.AddSeconds(11));
        Assert.Equal(AwsRefreshStatus.Done, r.Status);
    }

    [Fact]
    public void Snapshot_saved_before_the_script_exits_is_not_undone_by_heartbeat_or_a_late_failure()
    {
        // Card 78720: retrato gravado (PUT) → heartbeat do mesmo executor → report "failed" do script interrompido.
        var r = New(Alex);
        r.Claim(Alex, AlexMac, Now);
        Assert.True(r.CompleteBySnapshot(60_000, Now.AddSeconds(57)));
        Assert.True(r.Heartbeat(AlexMac, Now.AddSeconds(61)));   // não manda parar
        Assert.False(r.Heartbeat(BiaMac, Now.AddSeconds(61)));   // outro executor continua de fora
        r.Report(Alex, AlexMac, "failed", "A coleta passou de 20 min.", 61_000, Now.AddSeconds(62));
        Assert.Equal(AwsRefreshStatus.Done, r.Status);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Deploying_snapshot_schedules_a_follow_for_the_same_requester()
    {
        var finished = New(Alex, follow: 2);
        finished.Claim(Alex, AlexMac, Now);
        finished.CompleteBySnapshot(null, Now);

        // Quem gravou o retrato (Bia) não muda o dono: o acompanhamento segue no executor de quem pediu.
        var follow = AwsRefreshQueue.Follow("78720", true, finished, Bia, "Bia", false, 2, 15, Now);
        Assert.NotNull(follow);
        Assert.Equal(Alex, follow!.RequestedByUserId);
        Assert.Equal(AwsRefreshSource.Follow, follow.Source);
        Assert.Equal(3, follow.FollowCount);
        Assert.Equal(Now.AddMinutes(2), follow.NotBefore);
        Assert.Equal(AwsRefreshStatus.Queued, follow.Status);
    }

    [Fact]
    public void Follow_stops_at_the_max_when_nothing_is_deploying_or_with_an_active_request()
    {
        var atMax = New(Alex, follow: 15);
        Assert.Null(AwsRefreshQueue.Follow("78720", true, atMax, Alex, "Alex", false, 2, 15, Now));
        Assert.Null(AwsRefreshQueue.Follow("78720", false, New(Alex), Alex, "Alex", false, 2, 15, Now));
        Assert.Null(AwsRefreshQueue.Follow("78720", true, New(Alex), Alex, "Alex", true, 2, 15, Now));
        Assert.Null(AwsRefreshQueue.Follow("78720", true, New(Alex), Alex, "Alex", false, 2, 0, Now));
    }

    [Fact]
    public void Manual_collection_follows_for_whoever_collected()
    {
        var follow = AwsRefreshQueue.Follow("78720", true, null, Bia, "Bia", false, 2, 15, Now);
        Assert.Equal(Bia, follow!.RequestedByUserId);
        Assert.Equal(1, follow.FollowCount);
        Assert.Null(AwsRefreshQueue.Follow("78720", true, null, null, "?", false, 2, 15, Now));
    }

    [Fact]
    public void Rejects_bad_source_and_missing_requester()
    {
        Assert.Throws<DomainException>(() => new AwsRefresh("78720", Alex, "Alex", "cron", 0, null, Now));
        Assert.Throws<DomainException>(() => new AwsRefresh("78720", Guid.Empty, "Alex", AwsRefreshSource.Button, 0, null, Now));
        Assert.Throws<DomainException>(() => new AwsRefresh(" ", Alex, "Alex", AwsRefreshSource.Button, 0, null, Now));
    }
}
