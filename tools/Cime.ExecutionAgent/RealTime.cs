using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Cime.ExecutionAgent;

/// <summary>
/// 0068: o executor fica conectado ao relay de tempo real (hub SignalR fora do Cloud Run, 0013) e só consulta a fila
/// quando a API avisa (<c>executionQueueReady</c> no grupo dos executores do dono) — consulta periódica mantinha a
/// <c>cime-pullrequest</c> cobrada. Cliente mínimo do protocolo JSON do SignalR (negotiate + WebSocket), sem o pacote
/// do SignalR: o executor é publicado trimmed e sem reflexão no JSON.
/// </summary>
public sealed class RelayListener(PrmakeClient client, Action wake)
{
    private const char RecordSeparator = '\u001e';
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(15);
    /// <summary>O hub manda ping a cada 15 s: nada nesse tempo = conexão morta (rede caiu sem fechar o socket).</summary>
    private static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(60);
    /// <summary>API sem relay (desenvolvimento): pergunta de novo de tempos em tempos.</summary>
    private static readonly TimeSpan NoRelayRetry = TimeSpan.FromMinutes(30);

    private static readonly HttpClient Http = CreateHttp();

    private volatile bool _connected;
    private bool _warnedNoRelay;

    /// <summary>Conectado e no grupo: o laço principal pode esperar o aviso em vez de consultar a fila.</summary>
    public bool Connected => _connected;

    public async Task RunAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            TimeSpan retry;
            try
            {
                var info = await client.RealTimeAsync(ct);
                if (info?.Url is not { Length: > 0 } || info.Group.Length == 0 || info.Event.Length == 0)
                {
                    if (!_warnedNoRelay)
                        Log.Info("o PRMake não informou relay de tempo real — consultando a fila a cada 10 s");
                    _warnedNoRelay = true;
                    retry = NoRelayRetry;
                }
                else
                {
                    var wasConnected = await ListenAsync(info, ct);
                    failures = wasConnected ? 0 : failures + 1;
                    retry = Backoff(failures);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                failures++;
                retry = Backoff(failures);
                Log.Warn($"tempo real indisponível ({e.Message}) — consultando a fila a cada 10 s; nova conexão em {retry.TotalSeconds:0} s");
            }
            finally
            {
                _connected = false;
            }
            try
            {
                await Task.Delay(retry, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static TimeSpan Backoff(int failures) =>
        TimeSpan.FromSeconds(failures == 0 ? 2 : Math.Min(300, 2 << Math.Min(failures, 8)));

    /// <summary>Uma conexão, do negotiate até cair. True quando ficou de pé por um tempo (queda logo depois de entrar conta como falha).</summary>
    private async Task<bool> ListenAsync(RealTimeInfo info, CancellationToken ct)
    {
        var hub = new Uri(info.Url!);
        var connectionToken = await NegotiateAsync(hub, info.AccessToken, ct);

        var query = $"id={Uri.EscapeDataString(connectionToken)}";
        if (info.AccessToken is { Length: > 0 } token)
            query += $"&access_token={Uri.EscapeDataString(token)}";
        var target = new UriBuilder(hub) { Scheme = hub.Scheme == Uri.UriSchemeHttps ? "wss" : "ws", Query = query }.Uri;

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(target, ct);
        var reader = new RecordReader(ws);

        await SendAsync(ws, """{"protocol":"json","version":1}""", ct);
        var handshake = await reader.NextAsync(ct) ?? throw new IOException("o relay fechou a conexão no handshake");
        using (var doc = JsonDocument.Parse(handshake))
        {
            if (doc.RootElement.TryGetProperty("error", out var error))
                throw new IOException($"handshake recusado: {error.GetString()}");
        }

        var group = JsonSerializer.Serialize(info.Group, AgentJson.Default.String);
        await SendAsync(ws, $$"""{"type":1,"target":"AddToGroup","arguments":[{{group}}]}""", ct);
        _connected = true;
        var connectedAt = DateTimeOffset.UtcNow;
        Log.Info("conectado ao tempo real — a fila é consultada quando o PRMake avisar");
        // o que entrou na fila enquanto estava desconectado
        wake();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pings = PingAsync(ws, stop.Token);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var silence = CancellationTokenSource.CreateLinkedTokenSource(ct);
                silence.CancelAfter(SilenceLimit);
                string? record;
                try
                {
                    record = await reader.NextAsync(silence.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Log.Warn("tempo real sem resposta — reconectando");
                    break;
                }
                if (record is null) break;

                using var doc = JsonDocument.Parse(record);
                var type = doc.RootElement.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
                if (type == 1 && doc.RootElement.TryGetProperty("target", out var name) && name.GetString() == info.Event)
                    wake();
                else if (type == 7)
                {
                    if (doc.RootElement.TryGetProperty("error", out var error))
                        Log.Warn($"o relay encerrou a conexão: {error.GetString()}");
                    break;
                }
            }
        }
        finally
        {
            _connected = false;
            // o laço principal esperava o aviso (reserva de 10 min): volta a consultar a cada 10 s até reconectar
            wake();
            await stop.CancelAsync();
            await pings;
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    using var close = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, close.Token);
                }
                catch
                {
                    // já caiu
                }
            }
        }
        return DateTimeOffset.UtcNow - connectedAt >= TimeSpan.FromMinutes(1);
    }

    private static async Task<string> NegotiateAsync(Uri hub, string? accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{hub.GetLeftPart(UriPartial.Path).TrimEnd('/')}/negotiate?negotiateVersion=1")
        {
            Content = new ByteArrayContent([])
        };
        if (accessToken is { Length: > 0 })
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"negotiate HTTP {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new IOException($"negotiate: {error.GetString()}");
        var webSockets = root.TryGetProperty("availableTransports", out var transports) && transports.ValueKind == JsonValueKind.Array
                         && transports.EnumerateArray().Any(x => x.TryGetProperty("transport", out var n) && n.GetString() == "WebSockets");
        if (!webSockets)
            throw new IOException("o relay não oferece WebSocket");
        return (root.TryGetProperty("connectionToken", out var token) ? token.GetString() : null)
               ?? root.GetProperty("connectionId").GetString()
               ?? throw new IOException("negotiate sem connectionToken");
    }

    private static async Task PingAsync(ClientWebSocket ws, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                await Task.Delay(PingEvery, ct);
                await SendAsync(ws, """{"type":6}""", ct);
            }
        }
        catch
        {
            // a leitura percebe a queda e reconecta
        }
    }

    private static Task SendAsync(ClientWebSocket ws, string message, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(message + RecordSeparator), WebSocketMessageType.Text, true, ct);

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"prmake-agent/{Agent.Version}");
        return http;
    }

    /// <summary>Registros do protocolo (JSON terminado em 0x1E): vários por frame ou um partido em vários frames.</summary>
    private sealed class RecordReader(ClientWebSocket ws)
    {
        private readonly StringBuilder _pending = new();
        private readonly byte[] _buffer = new byte[8192];
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

        /// <summary>Próximo registro; null quando a conexão fechou.</summary>
        public async Task<string?> NextAsync(CancellationToken ct)
        {
            while (true)
            {
                var text = _pending.ToString();
                var end = text.IndexOf(RecordSeparator);
                if (end >= 0)
                {
                    _pending.Remove(0, end + 1);
                    return text[..end];
                }

                var result = await ws.ReceiveAsync(_buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                var chars = new char[_decoder.GetCharCount(_buffer, 0, result.Count)];
                _decoder.GetChars(_buffer, 0, result.Count, chars, 0);
                _pending.Append(chars);
            }
        }
    }
}
