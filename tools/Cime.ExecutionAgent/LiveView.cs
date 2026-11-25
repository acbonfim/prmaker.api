using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cime.ExecutionAgent;

/// <summary>
/// 0095: "Ver tela ao vivo" — a Bancada aberta NESTA máquina mostra o navegador da skill validar-ambiente enquanto o
/// Claude navega. O daemon do navegador (env_browser.py) abre o Chrome com uma porta de depuração só em 127.0.0.1 e
/// grava na sessão (~/.prmake/browser/sessions) a porta, a aba e o card; este servidor, também só em 127.0.0.1, lista as
/// sessões abertas e transmite a aba em MJPEG (Page.startScreencast) enquanto alguém assiste. Nada sai da máquina nem é
/// gravado: sem quem assista, a transmissão para. A lista só responde às origens do PRMake (CORS); o vídeo exige o
/// token da lista (gerado a cada início do executor). HTTP mínimo sobre TcpListener: o HttpListener pede URL ACL no Windows.
/// </summary>
public sealed class LiveView(AgentConfig config)
{
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(10);
    /// <summary>No máximo ~5 quadros/s por espectador: tela com animação manda 20+ por segundo e não muda nada para quem olha.</summary>
    private static readonly TimeSpan MinFrameGap = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RetryBind = TimeSpan.FromMinutes(5);

    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private readonly ConcurrentDictionary<string, Caster> _casters = new();
    private readonly object _gate = new();

    public static string SessionsDir =>
        Environment.GetEnvironmentVariable("PRMAKE_BROWSER_DIR") is { Length: > 0 } d
            ? Path.Combine(d, "sessions")
            : Path.Combine(Paths.PrmakeHome, "browser", "sessions");

    public async Task RunAsync(CancellationToken ct)
    {
        if (config.LiveViewPort <= 0) return;
        while (!ct.IsCancellationRequested)
        {
            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, config.LiveViewPort);
                listener.Start();
            }
            catch (SocketException e)
            {
                Log.Warn($"tela ao vivo indisponível: porta {config.LiveViewPort} ocupada ({e.Message}) — nova tentativa em {RetryBind.TotalMinutes:0} min");
                try { await Task.Delay(RetryBind, ct); } catch (OperationCanceledException) { return; }
                continue;
            }
            Log.Info($"tela ao vivo em http://127.0.0.1:{config.LiveViewPort} (só esta máquina)");
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => HandleAsync(client, ct), ct);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Log.Warn($"tela ao vivo caiu ({e.Message}) — reabrindo");
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────────────────────────────────

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var owned = client;
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, ct);
            if (request is null) return;
            var (method, target, headers) = request.Value;
            var origin = headers.GetValueOrDefault("origin");
            var allowed = origin is not null && config.LiveViewOrigins.Any(o => string.Equals(o.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase));
            var cors = allowed ? $"Access-Control-Allow-Origin: {origin}\r\nVary: Origin\r\n" : string.Empty;
            // Defesa contra DNS rebinding: só 127.0.0.1/localhost no Host.
            var host = headers.GetValueOrDefault("host") ?? string.Empty;
            if (!host.StartsWith("127.0.0.1:", StringComparison.Ordinal) && !host.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(stream, 403, "text/plain", "host"u8.ToArray(), string.Empty, ct);
                return;
            }

            var path = target.Split('?', 2)[0];
            var query = target.Contains('?') ? target.Split('?', 2)[1] : string.Empty;
            if (method == "OPTIONS")
            {
                var pre = allowed
                    ? cors + "Access-Control-Allow-Methods: GET\r\nAccess-Control-Allow-Headers: *\r\nAccess-Control-Allow-Private-Network: true\r\nAccess-Control-Max-Age: 600\r\n"
                    : string.Empty;
                await WriteAsync(stream, allowed ? 204 : 403, null, [], pre, ct);
                return;
            }
            if (method != "GET")
            {
                await WriteAsync(stream, 405, "text/plain", "method"u8.ToArray(), cors, ct);
                return;
            }
            if (path == "/live/sessions")
            {
                if (!allowed)
                {
                    await WriteAsync(stream, 403, "text/plain", "origin"u8.ToArray(), string.Empty, ct);
                    return;
                }
                await WriteAsync(stream, 200, "application/json", SessionsJson(), cors + "Cache-Control: no-store\r\n", ct);
                return;
            }
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts is ["live", var sid, "stream"])
            {
                if (QueryValue(query, "t") != _token)
                {
                    await WriteAsync(stream, 403, "text/plain", "token"u8.ToArray(), cors, ct);
                    return;
                }
                var session = ReadSessions().FirstOrDefault(s => s.Sid == Uri.UnescapeDataString(sid));
                if (session is null)
                {
                    await WriteAsync(stream, 404, "text/plain", "sessao"u8.ToArray(), cors, ct);
                    return;
                }
                await StreamAsync(stream, session, cors, ct);
                return;
            }
            await WriteAsync(stream, 404, "text/plain", "nada aqui"u8.ToArray(), cors, ct);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // quem assistia fechou a tela
        }
        catch (Exception e)
        {
            Log.Warn($"tela ao vivo: {e.Message}");
        }
    }

    private static async Task<(string Method, string Target, Dictionary<string, string> Headers)?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var buffer = new byte[8192];
        var length = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token);
            if (read == 0) return null;
            length += read;
            var text = Encoding.ASCII.GetString(buffer, 0, length);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0)
            {
                var lines = text[..end].Split("\r\n");
                var first = lines[0].Split(' ');
                if (first.Length < 2) return null;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1))
                    if (line.IndexOf(':') is var i and > 0)
                        headers[line[..i].Trim()] = line[(i + 1)..].Trim();
                return (first[0].ToUpperInvariant(), first[1], headers);
            }
            if (length == buffer.Length) return null;
        }
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string? contentType, byte[] body, string extraHeaders, CancellationToken ct)
    {
        var reason = status switch { 200 => "OK", 204 => "No Content", 403 => "Forbidden", 404 => "Not Found", _ => "Method Not Allowed" };
        var head = $"HTTP/1.1 {status} {reason}\r\n" +
                   (contentType is null ? string.Empty : $"Content-Type: {contentType}\r\n") +
                   $"Content-Length: {body.Length}\r\n{extraHeaders}Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        if (body.Length > 0) await stream.WriteAsync(body, ct);
    }

    private static string? QueryValue(string query, string name) =>
        query.Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0] == name)
            .Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault();

    // ── Sessões do navegador ─────────────────────────────────────────────────────────────────────────────

    public sealed record BrowserSession(string Sid, string? Card, string? Task, string? Env, string? Label, bool ReadOnly,
        string? Started, int CdpPort, string TargetId);

    /// <summary>Sessões abertas (processo vivo) com porta de depuração — as de antes da 0095 não têm e ficam de fora.</summary>
    public static List<BrowserSession> ReadSessions()
    {
        var list = new List<BrowserSession>();
        if (!Directory.Exists(SessionsDir)) return list;
        foreach (var file in Directory.EnumerateFiles(SessionsDir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var m = doc.RootElement;
                string? Str(string k) => m.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : null;
                if (Str("status") != "ready" || !int.TryParse(Str("pid"), out var pid) || !Alive(pid)) continue;
                if (!int.TryParse(Str("cdpPort"), out var port) || Str("targetId") is not { Length: > 0 } target) continue;
                list.Add(new BrowserSession(Str("sid") ?? Path.GetFileNameWithoutExtension(file), Str("card"), Str("task"), Str("env"),
                    Str("label"), m.TryGetProperty("readOnly", out var ro) && ro.ValueKind == JsonValueKind.True, Str("started"), port, target));
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                // sessão sendo regravada agora — aparece na próxima consulta
            }
        }
        return list;
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private byte[] SessionsJson()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("worker", config.Name);
            w.WriteString("token", _token);
            w.WriteStartArray("sessions");
            foreach (var s in ReadSessions())
            {
                w.WriteStartObject();
                w.WriteString("sid", s.Sid);
                w.WriteString("card", s.Card);
                w.WriteString("task", s.Task);
                w.WriteString("env", s.Env);
                w.WriteString("label", s.Label);
                w.WriteBoolean("readOnly", s.ReadOnly);
                w.WriteString("started", s.Started);
                w.WriteString("stream", $"/live/{Uri.EscapeDataString(s.Sid)}/stream");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }

    // ── Transmissão ──────────────────────────────────────────────────────────────────────────────────────

    private async Task StreamAsync(NetworkStream stream, BrowserSession session, string cors, CancellationToken ct)
    {
        Caster caster;
        lock (_gate)
        {
            caster = _casters.GetOrAdd(session.Sid, _ => new Caster(session, c => _casters.TryRemove(new KeyValuePair<string, Caster>(session.Sid, c))));
            caster.Join();
        }
        try
        {
            var head = "HTTP/1.1 200 OK\r\nContent-Type: multipart/x-mixed-replace; boundary=frame\r\n" +
                       $"Cache-Control: no-store\r\n{cors}Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
            long seen = -1;
            while (!ct.IsCancellationRequested && !caster.Ended)
            {
                var (frame, version) = await caster.NextAsync(seen, KeepAlive, ct);
                if (frame is null) continue;
                // sem quadro novo em 10 s: repete o último (descobre quem fechou a tela)
                seen = version;
                var part = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(part), ct);
                await stream.WriteAsync(frame, ct);
                await stream.WriteAsync("\r\n"u8.ToArray(), ct);
                await stream.FlushAsync(ct);
                await Task.Delay(MinFrameGap, ct);
            }
        }
        finally
        {
            lock (_gate) caster.Leave();
        }
    }

    /// <summary>
    /// Uma conexão CDP por sessão, compartilhada por quem assiste: liga o screencast na primeira pessoa e desliga na
    /// última. Headless só manda quadro quando a tela muda — o primeiro vem de um Page.captureScreenshot.
    /// </summary>
    private sealed class Caster(BrowserSession session, Action<Caster> removed)
    {
        private readonly CancellationTokenSource _stop = new();
        private int _viewers;
        private byte[]? _frame;
        private long _version;
        private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _loop;

        public bool Ended { get; private set; }

        public void Join()
        {
            _viewers++;
            _loop ??= Task.Run(RunAsync);
        }

        public void Leave()
        {
            if (--_viewers > 0) return;
            removed(this);
            _stop.Cancel();
        }

        public async Task<(byte[]? Frame, long Version)> NextAsync(long seen, TimeSpan wait, CancellationToken ct)
        {
            Task signal;
            lock (this)
            {
                if (_frame is not null && _version != seen) return (_frame, _version);
                signal = _next.Task;
            }
            await Task.WhenAny(signal, Task.Delay(wait, ct));
            ct.ThrowIfCancellationRequested();
            lock (this)
                return _frame is not null && (_version != seen || !signal.IsCompleted) ? (_frame, _version) : (null, seen);
        }

        private void Publish(byte[] frame)
        {
            TaskCompletionSource done;
            lock (this)
            {
                _frame = frame;
                _version++;
                done = _next;
                _next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            done.TrySetResult();
        }

        private async Task RunAsync()
        {
            var ct = _stop.Token;
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{session.CdpPort}/devtools/page/{session.TargetId}"), ct);
                await SendAsync(ws, """{"id":1,"method":"Page.captureScreenshot","params":{"format":"jpeg","quality":60}}""", ct);
                await SendAsync(ws, """{"id":2,"method":"Page.startScreencast","params":{"format":"jpeg","quality":60,"maxWidth":1440,"maxHeight":900}}""", ct);
                var buffer = new byte[64 * 1024];
                using var message = new MemoryStream();
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    using var doc = JsonDocument.Parse(message.ToArray());
                    var root = doc.RootElement;
                    if (root.TryGetProperty("method", out var method) && method.GetString() == "Page.screencastFrame"
                        && root.TryGetProperty("params", out var p))
                    {
                        Publish(Convert.FromBase64String(p.GetProperty("data").GetString() ?? string.Empty));
                        var id = p.GetProperty("sessionId").GetInt32();
                        await SendAsync(ws, """{"id":3,"method":"Page.screencastFrameAck","params":{"sessionId":""" + id + "}}", ct);
                    }
                    else if (root.TryGetProperty("id", out var rid) && rid.GetInt32() == 1
                             && root.TryGetProperty("result", out var r) && r.TryGetProperty("data", out var data))
                    {
                        Publish(Convert.FromBase64String(data.GetString() ?? string.Empty));
                    }
                    else if (root.TryGetProperty("method", out var m) && m.GetString() is "Inspector.detached" or "Inspector.targetCrashed")
                    {
                        return;
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException)
            {
                // ninguém mais assiste ou o navegador fechou
            }
            catch (Exception e)
            {
                Log.Warn($"tela ao vivo ({session.Sid}): {e.Message}");
            }
            finally
            {
                Ended = true;
                lock (this) _next.TrySetResult();
                removed(this);
            }
        }

        private static Task SendAsync(ClientWebSocket ws, string json, CancellationToken ct) =>
            ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);
    }
}
