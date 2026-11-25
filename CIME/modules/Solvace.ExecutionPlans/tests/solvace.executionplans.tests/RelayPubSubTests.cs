using System.Text.Json;
using Cime.BuildingBlocks.RealTime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Relay pelo Pub/Sub (2026-11-25): de parte dos IPs de saída do Cloud Run o relay não abre conexão (instância inteira sem
/// tempo real). Com o tópico configurado, a API publica no Pub/Sub e nunca chama o relay.
/// </summary>
public class RelayPubSubTests
{
    private sealed class FakePublisher(Func<CancellationToken, Task>? behavior = null) : IRealTimePubSubPublisher
    {
        public List<byte[]> Sent { get; } = [];

        public async Task PublishAsync(byte[] data, CancellationToken cancellationToken)
        {
            if (behavior is not null) await behavior(cancellationToken);
            Sent.Add(data);
        }
    }

    private static IServiceProvider Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRealTimeService(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Relay_with_topic_publishes_via_pubsub()
    {
        var provider = Services(new()
        {
            ["RealTime:Mode"] = "Relay",
            ["RealTime:RelayUrl"] = "https://relay.example",
            ["RealTime:PubSubTopic"] = "projects/p/topics/t"
        });
        Assert.IsType<PubSubRealTimeNotifier>(provider.GetRequiredService<IRealTimeNotifier>());
    }

    [Fact]
    public void Relay_without_topic_keeps_the_http_publish()
    {
        var provider = Services(new() { ["RealTime:Mode"] = "Relay", ["RealTime:RelayUrl"] = "https://relay.example" });
        Assert.IsType<RelayRealTimeNotifier>(provider.GetRequiredService<IRealTimeNotifier>());
    }

    [Fact]
    public async Task Message_has_the_publish_body_in_camel_case()
    {
        var publisher = new FakePublisher();
        var health = new RealTimeDeliveryHealth(TimeProvider.System);
        var notifier = new PubSubRealTimeNotifier(publisher, NullLogger<PubSubRealTimeNotifier>.Instance, health);

        await notifier.NotifyGroupAsync("execplan:E1093", "caseChatUpdated", new { CardNumber = "E1093", Action = "reply" });

        var json = JsonDocument.Parse(Assert.Single(publisher.Sent)).RootElement;
        Assert.Equal("execplan:E1093", json.GetProperty("group").GetString());
        Assert.Equal("caseChatUpdated", json.GetProperty("event").GetString());
        Assert.Equal("E1093", json.GetProperty("payload").GetProperty("cardNumber").GetString());
        Assert.False(health.IsDegraded(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Broadcast_goes_without_group()
    {
        var publisher = new FakePublisher();
        await new PubSubRealTimeNotifier(publisher, NullLogger<PubSubRealTimeNotifier>.Instance).NotifyAllAsync("epicsUpdated");

        var json = JsonDocument.Parse(Assert.Single(publisher.Sent)).RootElement;
        Assert.Equal(JsonValueKind.Null, json.GetProperty("group").ValueKind);
    }

    [Fact]
    public async Task Failure_is_logged_and_reported_never_thrown()
    {
        var publisher = new FakePublisher(_ => throw new InvalidOperationException("pubsub fora"));
        var health = new RealTimeDeliveryHealth(TimeProvider.System);

        await new PubSubRealTimeNotifier(publisher, NullLogger<PubSubRealTimeNotifier>.Instance, health)
            .NotifyGroupAsync("g", "e");

        Assert.True(health.IsDegraded(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Stuck_publish_is_cut_by_the_timeout()
    {
        var publisher = new FakePublisher(ct => Task.Delay(Timeout.Infinite, ct));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await new PubSubRealTimeNotifier(publisher, NullLogger<PubSubRealTimeNotifier>.Instance).NotifyGroupAsync("g", "e");

        Assert.True(watch.Elapsed < PubSubRealTimeNotifier.PublishTimeout + TimeSpan.FromSeconds(2), $"{watch.Elapsed}");
    }
}
