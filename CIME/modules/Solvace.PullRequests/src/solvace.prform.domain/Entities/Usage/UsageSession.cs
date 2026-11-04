namespace solvace.prform.domain.Entities.Usage;

/// <summary>
/// Uma aba do navegador com a aplicação aberta (0079). Criada/atualizada pelos lotes de uso (<c>POST Usage/batch</c>):
/// os contadores de tempo vêm do navegador e só crescem (vale o maior valor recebido).
/// </summary>
public class UsageSession
{
    public const int MaxUserNameLength = 200;
    public const int MaxIpLength = 64;
    public const int MaxUserAgentLength = 512;
    public const int MaxShortLength = 60;
    public const int MaxScreenLength = 120;
    public const int MaxRouteLength = 200;

    public Guid Id { get; private set; }
    public Guid? UserExternalId { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    /// <summary>Quando a aba avisou que fechou (pagehide/logout); null enquanto aberta ou se fechou sem avisar.</summary>
    public DateTimeOffset? EndedAt { get; private set; }
    public string? Ip { get; private set; }
    public string? UserAgent { get; private set; }
    public string? Browser { get; private set; }
    public string? Os { get; private set; }
    public string? Device { get; private set; }
    public string? ScreenSize { get; private set; }
    public string? Language { get; private set; }
    public string? TimeZone { get; private set; }
    public string? AppVersion { get; private set; }
    public string? LastScreen { get; private set; }
    public string? LastRoute { get; private set; }
    public long ForegroundMs { get; private set; }
    public long BackgroundMs { get; private set; }
    public long AwayMs { get; private set; }
    public int Views { get; private set; }
    public int Actions { get; private set; }
    public int Events { get; private set; }

    protected UsageSession() { }

    public UsageSession(Guid id, Guid? userExternalId, string? userName, DateTimeOffset startedAt)
    {
        Id = id;
        UserExternalId = userExternalId;
        UserName = UsageText.Cut(userName, MaxUserNameLength) ?? string.Empty;
        StartedAt = startedAt;
        LastSeenAt = startedAt;
    }

    public void Report(DateTimeOffset now, string? ip, string? userAgent, string? browser, string? os, string? device, string? screenSize,
        string? language, string? timeZone, string? appVersion, string? screen, string? route, long foregroundMs, long backgroundMs,
        long awayMs, bool ended, int views, int actions, int events)
    {
        if (now > LastSeenAt) LastSeenAt = now;
        Ip = UsageText.Cut(ip, MaxIpLength) ?? Ip;
        UserAgent = UsageText.Cut(userAgent, MaxUserAgentLength) ?? UserAgent;
        Browser = UsageText.Cut(browser, MaxShortLength) ?? Browser;
        Os = UsageText.Cut(os, MaxShortLength) ?? Os;
        Device = UsageText.Cut(device, MaxShortLength) ?? Device;
        ScreenSize = UsageText.Cut(screenSize, MaxShortLength) ?? ScreenSize;
        Language = UsageText.Cut(language, MaxShortLength) ?? Language;
        TimeZone = UsageText.Cut(timeZone, MaxShortLength) ?? TimeZone;
        AppVersion = UsageText.Cut(appVersion, MaxShortLength) ?? AppVersion;
        LastScreen = UsageText.Cut(screen, MaxScreenLength) ?? LastScreen;
        LastRoute = UsageText.Cut(route, MaxRouteLength) ?? LastRoute;
        ForegroundMs = Math.Max(ForegroundMs, Math.Max(0, foregroundMs));
        BackgroundMs = Math.Max(BackgroundMs, Math.Max(0, backgroundMs));
        AwayMs = Math.Max(AwayMs, Math.Max(0, awayMs));
        Views += Math.Max(0, views);
        Actions += Math.Max(0, actions);
        Events += Math.Max(0, events);
        EndedAt = ended ? now : null;
    }
}

/// <summary>Um passo do usuário (0079): tela visitada, clique, ação na API, erro, leitura lenta, ação nomeada, login/logout.</summary>
public class UsageEvent
{
    public const int MaxKindLength = 16;
    public const int MaxScreenLength = 120;
    public const int MaxRouteLength = 200;
    public const int MaxCardLength = 40;
    public const int MaxNameLength = 120;
    public const int MaxDetailLength = 300;

    public static readonly IReadOnlySet<string> Kinds =
        new HashSet<string> { "view", "click", "action", "error", "slow", "track", "login", "logout" };

    public long Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid? UserExternalId { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public DateTimeOffset At { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string? Screen { get; private set; }
    public string? Route { get; private set; }
    public string? Card { get; private set; }
    public string? Name { get; private set; }
    public string? Detail { get; private set; }
    public int? DurationMs { get; private set; }
    public int? Status { get; private set; }

    protected UsageEvent() { }

    public UsageEvent(Guid sessionId, Guid? userExternalId, string? userName, DateTimeOffset at, string kind, string? screen, string? route,
        string? card, string? name, string? detail, int? durationMs, int? status)
    {
        SessionId = sessionId;
        UserExternalId = userExternalId;
        UserName = UsageText.Cut(userName, UsageSession.MaxUserNameLength) ?? string.Empty;
        At = at;
        Kind = kind;
        Screen = UsageText.Cut(screen, MaxScreenLength);
        Route = UsageText.Cut(route, MaxRouteLength);
        Card = UsageText.Cut(card, MaxCardLength);
        Name = UsageText.Cut(name, MaxNameLength);
        Detail = UsageText.Cut(detail, MaxDetailLength);
        DurationMs = durationMs is { } d ? Math.Max(0, d) : null;
        Status = status;
    }
}

/// <summary>Tentativa de login (0079) — tabela <c>auth.LoginEvents</c> da Cime.Auth, lida pelo AuthenticationContext.</summary>
public class LoginEventRecord
{
    public long Id { get; set; }
    /// <summary>Horário do servidor da auth (UTC no Cloud Run), sem fuso.</summary>
    public DateTime At { get; set; }
    public int? UserId { get; set; }
    public Guid? UserExternalId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
}

public static class UsageText
{
    public static string? Cut(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length <= max ? v : v[..max];
    }
}
