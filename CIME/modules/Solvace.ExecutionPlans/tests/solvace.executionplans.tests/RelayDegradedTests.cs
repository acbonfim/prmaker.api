using Cime.BuildingBlocks.RealTime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Relay fora (2026-11-25, 28 falhas na hora): cada aviso segurava a requisição 3 s — a pergunta da Base levou 11 s para entrar
/// na fila e o executor 13 s para pegá-la. Com a última publicação falhando, as seguintes esperam só o tempo curto (menos
/// uma sonda a cada 15 s), e a primeira que passa tira do modo degradado.
/// </summary>
public class RelayDegradedTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Relay que não responde (espera até ser cancelado) ou responde 202 na hora.</summary>
    private sealed class Relay : HttpMessageHandler
    {
        public bool Down { get; set; } = true;
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (Down) await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(3) };
    }

    private sealed class Options : IRealTimeOptionsProvider
    {
        public RealTimeOptions GetOptions() => new() { RelayUrl = "http://relay.test" };
    }

    [Fact]
    public void Degraded_only_while_the_last_publication_failed()
    {
        var clock = new Clock(DateTimeOffset.Parse("2026-11-25T13:51:00Z"));
        var health = new RealTimeDeliveryHealth(clock);
        Assert.False(health.IsDegraded(TimeSpan.FromMinutes(1)));

        health.ReportFailure();
        Assert.True(health.IsDegraded(TimeSpan.FromMinutes(1)));

        clock.Now = clock.Now.AddSeconds(5);
        health.ReportSuccess();
        Assert.False(health.IsDegraded(TimeSpan.FromMinutes(1)));
        // o executor continua voltando mais cedo pela janela longa (o relay está instável)
        Assert.True(health.FailedWithin(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Only_one_probe_every_fifteen_seconds()
    {
        var clock = new Clock(DateTimeOffset.Parse("2026-11-25T13:51:00Z"));
        var health = new RealTimeDeliveryHealth(clock);
        Assert.True(health.TryTakeProbe());
        Assert.False(health.TryTakeProbe());
        clock.Now = clock.Now.AddSeconds(15);
        Assert.True(health.TryTakeProbe());
    }

    [Fact]
    public async Task With_the_relay_down_no_request_waits_for_it_and_the_probe_runs_in_background()
    {
        var clock = new Clock(DateTimeOffset.UtcNow);
        var health = new RealTimeDeliveryHealth(clock);
        var relay = new Relay();
        var notifier = new RelayRealTimeNotifier(new Factory(relay), new Options(), NullLogger<RelayRealTimeNotifier>.Instance, health);
        health.ReportFailure();

        // 1ª publicação no modo degradado = a sonda (em segundo plano); as seguintes se perdem — nenhuma segura a requisição
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await notifier.NotifyGroupAsync("execplan:BQ-1", "executionPlanUpdated");
        await notifier.NotifyGroupAsync("execplan-pending", "executionWorkersChanged");
        await notifier.NotifyGroupAsync("execworkers:1", "executionQueueReady");
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500), $"levou {watch.Elapsed.TotalMilliseconds:0} ms");
        await WaitUntil(() => relay.Calls == 1);
        Assert.Equal(1, relay.Calls);

        // o relay voltou: a próxima sonda passa e tira do modo degradado
        relay.Down = false;
        clock.Now = clock.Now.AddSeconds(16);
        await notifier.NotifyGroupAsync("execplan:BQ-1", "executionPlanUpdated");
        await WaitUntil(() => !health.IsDegraded(TimeSpan.FromMinutes(1)));
        Assert.False(health.IsDegraded(TimeSpan.FromMinutes(1)));

        // e as publicações voltam a ir na hora
        await notifier.NotifyGroupAsync("execplan:BQ-1", "executionPlanUpdated");
        Assert.Equal(3, relay.Calls);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
    }
}
