using Cime.BuildingBlocks.RealTime;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Relay instável (2026-11-24: ~48% das publicações estouravam os 3 s): a falha recente faz o sinal de vida do executor
/// voltar mais cedo, e a janela fecha sozinha quando o relay volta.
/// </summary>
public class RealTimeDeliveryHealthTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Recent_failure_opens_the_window_and_it_closes_by_itself()
    {
        var clock = new Clock(DateTimeOffset.Parse("2026-11-24T23:58:00Z"));
        var health = new RealTimeDeliveryHealth(clock);
        Assert.Null(health.LastFailureAt);
        Assert.False(health.FailedWithin(TimeSpan.FromMinutes(10)));

        health.ReportFailure();
        clock.Now = clock.Now.AddMinutes(9);
        Assert.True(health.FailedWithin(TimeSpan.FromMinutes(10)));

        clock.Now = clock.Now.AddMinutes(2);
        Assert.False(health.FailedWithin(TimeSpan.FromMinutes(10)));
    }
}
