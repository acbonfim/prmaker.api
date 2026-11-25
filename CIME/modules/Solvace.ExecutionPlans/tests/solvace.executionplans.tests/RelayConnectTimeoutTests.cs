using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Cime.BuildingBlocks.RealTime;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Relay instável (2026-11-25): 46 de 93 publicações estouraram os 3 s em sequência, esperando uma tentativa de conexão
/// travada (o pool do .NET não abre outra enquanto há uma pendente para a única requisição na fila). Com o tempo de conexão
/// limitado, a tentativa morta cai sozinha e a publicação seguinte tenta uma conexão nova.
/// </summary>
public class RelayConnectTimeoutTests
{
    /// <summary>Aceita o TCP e nunca responde o TLS (a conexão "travada").</summary>
    private static (TcpListener Listener, Uri Url) StalledServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = Task.Run(async () =>
        {
            var held = new List<TcpClient>();
            try { while (true) held.Add(await listener.AcceptTcpClientAsync()); }
            catch { /* parado */ }
        });
        return (listener, new Uri($"https://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/publish"));
    }

    [Fact]
    public void Relay_client_limits_the_connection_time()
    {
        using var handler = RelayRealTimeNotifier.CreateHandler();
        Assert.Equal(TimeSpan.FromSeconds(2), handler.ConnectTimeout);
        Assert.True(RelayRealTimeNotifier.ConnectTimeout < RelayRealTimeNotifier.RequestTimeout);
    }

    [Fact]
    public async Task Stalled_connection_fails_fast_and_does_not_hold_the_next_publication()
    {
        var (listener, url) = StalledServer();
        try
        {
            var handler = RelayRealTimeNotifier.CreateHandler();
            handler.ConnectTimeout = TimeSpan.FromMilliseconds(300); // o mesmo mecanismo, em escala de teste
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            for (var i = 0; i < 2; i++)
            {
                var watch = Stopwatch.StartNew();
                await Assert.ThrowsAnyAsync<Exception>(() => client.PostAsync(url, new StringContent("{}")));
                watch.Stop();
                // falha pelo tempo de conexão (não pelos 5 s da requisição) — e a 2ª não herda a tentativa da 1ª
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"tentativa {i + 1} levou {watch.Elapsed.TotalMilliseconds:0} ms");
            }
        }
        finally
        {
            listener.Stop();
        }
    }
}
