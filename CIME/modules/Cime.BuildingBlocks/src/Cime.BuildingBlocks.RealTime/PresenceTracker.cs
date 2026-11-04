using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>Dados do navegador da aba (calculados no front).</summary>
public record PresenceClient(string? Browser, string? Os, string? Device, string? ScreenSize, string? Language,
    string? TimeZone, string? AppVersion);

/// <summary>O que a aba informa ao hub a cada mudança (UpdatePresence, 0079).</summary>
public record PresenceReport(string? SessionId, string? State, bool Visible, bool Focused, string? Screen, string? Route,
    string? Card, string? Busy, DateTimeOffset? LastInputAt, DateTimeOffset? Since, DateTimeOffset? StartedAt,
    string? FullName, PresenceClient? Client);

/// <summary>Uma aba conectada: o último relato + o que só o servidor sabe (quem é, IP, navegador, desde quando).</summary>
public record PresenceEntry(string ConnectionId, string? UserId, string? UserName, string? FullName, string? SessionId,
    string State, bool Visible, bool Focused, string? Screen, string? Route, string? Card, string? Busy,
    DateTimeOffset? LastInputAt, DateTimeOffset? Since, DateTimeOffset? StartedAt, DateTimeOffset ConnectedAt,
    DateTimeOffset UpdatedAt, string? Ip, string? UserAgent, PresenceClient? Client);

/// <summary>
/// Presença ao vivo (0079): quem está com a aplicação aberta e o que está fazendo, por conexão do hub. Fica em memória
/// onde o hub roda — no relay (MonsterASP, custo fixo) em produção —, então não custa requisição no Cloud Run. Some
/// quando a conexão cai; reinício do relay zera tudo e as abas relatam de novo ao reconectar.
/// </summary>
public class PresenceTracker(TimeProvider clock)
{
    /// <summary>Grupo do hub com quem acompanha a presença (só admin). Não pode ser assinado pelo AddToGroup.</summary>
    public const string WatchersGroup = "presence:watchers";
    public const string ReservedPrefix = "presence:";
    public const string ChangedEvent = "presenceChanged";
    public const string RemovedEvent = "presenceRemoved";

    public static readonly IReadOnlySet<string> States = new HashSet<string> { "active", "busy", "background", "away" };
    private const int MaxText = 200;
    private const int MaxEntries = 5000;

    private readonly ConcurrentDictionary<string, PresenceEntry> _entries = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _connectedAt = new();

    public void Connected(string connectionId) => _connectedAt.TryAdd(connectionId, clock.GetUtcNow());

    /// <summary>Guarda o relato da conexão; null quando descartado (limite de entradas).</summary>
    public PresenceEntry? Update(string connectionId, string? userId, string? userName, PresenceReport report, HttpContext? http)
    {
        if (!_entries.ContainsKey(connectionId) && _entries.Count >= MaxEntries) return null;
        var now = clock.GetUtcNow();
        var connectedAt = _connectedAt.GetOrAdd(connectionId, now);
        var state = report.State is { } s && States.Contains(s) ? s : "active";
        var client = report.Client is { } c
            ? new PresenceClient(Cut(c.Browser, 60), Cut(c.Os, 60), Cut(c.Device, 30), Cut(c.ScreenSize, 20), Cut(c.Language, 20),
                Cut(c.TimeZone, 60), Cut(c.AppVersion, 40))
            : null;
        var previous = _entries.TryGetValue(connectionId, out var p) ? p : null;
        var entry = new PresenceEntry(connectionId, Cut(userId, 64), Cut(userName, MaxText), Cut(report.FullName, MaxText) ?? previous?.FullName,
            Cut(report.SessionId, 64), state, report.Visible, report.Focused, Cut(report.Screen, MaxText), Cut(report.Route, MaxText),
            Cut(report.Card, 40), Cut(report.Busy, MaxText), Clamp(report.LastInputAt, now), Clamp(report.Since, now),
            Clamp(report.StartedAt, now), connectedAt, now,
            previous?.Ip ?? Cut(ClientAddress.From(http), 64),
            previous?.UserAgent ?? Cut(http?.Request.Headers.UserAgent.ToString(), 512), client ?? previous?.Client);
        _entries[connectionId] = entry;
        return entry;
    }

    /// <summary>Tira a conexão; devolve a entrada que existia (para avisar quem acompanha).</summary>
    public PresenceEntry? Disconnected(string connectionId)
    {
        _connectedAt.TryRemove(connectionId, out _);
        return _entries.TryRemove(connectionId, out var entry) ? entry : null;
    }

    public IReadOnlyList<PresenceEntry> Snapshot() => _entries.Values.OrderBy(e => e.UserName).ThenBy(e => e.ConnectedAt).ToList();

    private static DateTimeOffset? Clamp(DateTimeOffset? value, DateTimeOffset now) => value is { } v && v > now ? now : value;

    private static string? Cut(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length <= max ? v : v[..max];
    }
}

/// <summary>IP do cliente atrás de proxy (0079).</summary>
public static class ClientAddress
{
    /// <summary>
    /// Último salto do X-Forwarded-For (o que o proxy mais próximo acrescentou — o Cloud Run põe o IP do cliente no
    /// fim; os anteriores vêm do próprio cliente e podem ser forjados) ou o endereço da conexão.
    /// </summary>
    public static string? From(HttpContext? http)
    {
        if (http is null) return null;
        var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var last = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            if (!string.IsNullOrEmpty(last)) return last;
        }
        var remote = http.Connection.RemoteIpAddress;
        return remote is null ? null : (remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote).ToString();
    }
}
