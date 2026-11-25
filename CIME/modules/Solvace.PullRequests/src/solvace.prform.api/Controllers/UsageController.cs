using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cime.BuildingBlocks.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using solvace.prform.domain.Entities.Usage;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.Controllers;

/// <summary>
/// Dados de uso (0079). O navegador manda os passos do usuário em lote (<c>batch</c>: telas, cliques, ações, erros) — uma
/// requisição curta a cada poucos minutos, nada quando a aba está parada. A presença ao vivo NÃO passa por aqui: fica no
/// hub (relay), sem custo no Cloud Run. As leituras (admin) são sob demanda e agregam no banco.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class UsageController(DefaultContext db, AuthenticationContext auth, ILogger<UsageController> logger) : ControllerBase
{
    private const int MaxEventsPerBatch = 500;
    private const int RetentionDays = 180;
    private const int MaxRangeDays = 180;
    private const int MaxSessionsInSummary = 20000;
    private static readonly string[] ActionKinds = ["action", "track"];
    private static long _lastPurgeTicks;

    // ---------------------------------------------------------------- gravação (qualquer logado)

    /// <summary>Lote de uso da aba: atualiza a sessão (contadores de tempo, tela atual, navegador) e grava os eventos.</summary>
    [HttpPost("batch")]
    [RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> Batch([FromBody] UsageBatchRequest request, CancellationToken cancellationToken)
    {
        if (request?.Session is not { } s || s.Id == Guid.Empty) return BadRequest(new { message = "session.id obrigatório" });
        var (userId, userName) = CurrentUser(User);
        try
        {
            await SaveBatchAsync(s, request.Events ?? [], userId, userName, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dois lotes da mesma aba nova ao mesmo tempo: o segundo encontra a sessão criada pelo primeiro.
            db.ChangeTracker.Clear();
            await SaveBatchAsync(s, request.Events ?? [], userId, userName, cancellationToken);
        }
        await PurgeOldAsync(cancellationToken);
        return NoContent();
    }

    private async Task SaveBatchAsync(UsageSessionDto s, List<UsageEventDto> input, Guid? userId, string userName, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var sessionId = s.Id;
        var session = await db.UsageSessions.FindAsync([sessionId], ct);
        if (session is not null && session.UserExternalId != userId)
        {
            // Mesma aba, outro usuário (trocou de conta sem fechar a aba): sessão própria, estável entre os lotes.
            sessionId = DerivedSessionId(sessionId, userId);
            session = await db.UsageSessions.FindAsync([sessionId], ct);
        }
        if (session is null)
        {
            var startedAt = Clamp(s.StartedAt, now.AddDays(-2), now) ?? now;
            session = new UsageSession(sessionId, userId, userName, startedAt);
            db.UsageSessions.Add(session);
        }

        var minAt = session.StartedAt.AddMinutes(-5);
        var events = input
            .Where(e => e.Kind is { } k && UsageEvent.Kinds.Contains(k))
            .Take(MaxEventsPerBatch)
            .Select(e => new UsageEvent(sessionId, userId, userName, Clamp(e.At, minAt, now) ?? now, e.Kind!, e.Screen, e.Route, e.Card,
                e.Name, e.Detail, e.DurationMs, e.Status))
            .ToList();

        var c = s.Client;
        session.Report(now, ClientAddress.From(HttpContext), Request.Headers.UserAgent.ToString(), c?.Browser, c?.Os, c?.Device,
            c?.ScreenSize, c?.Language, c?.TimeZone, c?.AppVersion, s.Screen, s.Route, s.ForegroundMs, s.BackgroundMs, s.AwayMs, s.Ended,
            events.Count(e => e.Kind == "view"), events.Count(e => ActionKinds.Contains(e.Kind)), events.Count);
        db.UsageEvents.AddRange(events);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Retenção de <see cref="RetentionDays"/> dias: no máximo uma limpeza por dia por instância, no próprio lote.</summary>
    private async Task PurgeOldAsync(CancellationToken ct)
    {
        var last = Interlocked.Read(ref _lastPurgeTicks);
        var nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks - last < TimeSpan.TicksPerDay || Interlocked.CompareExchange(ref _lastPurgeTicks, nowTicks, last) != last) return;
        try
        {
            var limit = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
            var events = await db.UsageEvents.Where(e => e.At < limit).ExecuteDeleteAsync(ct);
            var sessions = await db.UsageSessions.Where(x => x.LastSeenAt < limit).ExecuteDeleteAsync(ct);
            if (events + sessions > 0) logger.LogInformation("Uso: {Events} eventos e {Sessions} sessões com mais de {Days} dias apagados", events, sessions, RetentionDays);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Não limpei os dados de uso antigos");
        }
    }

    // ---------------------------------------------------------------- leituras (admin, sob demanda)

    /// <summary>Visão geral do período: indicadores, série por dia, mapa de calor, telas, ações, erros, navegadores e por usuário.</summary>
    [HttpGet("summary")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Summary([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? user,
        [FromQuery] string? screen, [FromQuery] int tzOffsetMinutes = -180, CancellationToken cancellationToken = default)
    {
        var (start, end) = Range(from, to);
        var offset = TimeSpan.FromMinutes(Math.Clamp(tzOffsetMinutes, -14 * 60, 14 * 60));

        var events = db.UsageEvents.AsNoTracking().Where(e => e.At >= start && e.At <= end);
        if (user is { } u) events = events.Where(e => e.UserExternalId == u);
        var scoped = string.IsNullOrWhiteSpace(screen) ? events : events.Where(e => e.Screen == screen);

        // Uma linha por usuário × hora: dá a série por dia, o mapa de calor e os totais por usuário sem trazer eventos.
        var hourly = await scoped
            .GroupBy(e => new { e.At.Date, e.At.Hour, e.UserExternalId })
            .Select(g => new HourRow(g.Key.Date, g.Key.Hour, g.Key.UserExternalId, g.Count(),
                g.Count(e => e.Kind == "view"), g.Count(e => e.Kind == "action" || e.Kind == "track"), g.Count(e => e.Kind == "click"),
                g.Count(e => e.Kind == "error")))
            .ToListAsync(cancellationToken);

        var sessionsQuery = db.UsageSessions.AsNoTracking().Where(x => x.StartedAt <= end && x.LastSeenAt >= start);
        if (user is { } su) sessionsQuery = sessionsQuery.Where(x => x.UserExternalId == su);
        var sessions = await sessionsQuery
            .OrderByDescending(x => x.LastSeenAt)
            .Take(MaxSessionsInSummary)
            .Select(x => new SessionRow(x.UserExternalId, x.UserName, x.StartedAt, x.LastSeenAt, x.ForegroundMs, x.BackgroundMs, x.AwayMs,
                x.Browser, x.Os, x.Device))
            .ToListAsync(cancellationToken);

        var screens = await events.Where(e => e.Kind == "view")
            .GroupBy(e => e.Screen)
            .Select(g => new { screen = g.Key, views = g.Count(), durationMs = g.Sum(e => (long?)e.DurationMs) ?? 0,
                users = g.Select(e => e.UserExternalId).Distinct().Count() })
            .OrderByDescending(x => x.views).Take(30)
            .ToListAsync(cancellationToken);

        var actions = await scoped.Where(e => e.Kind == "action" || e.Kind == "track" || e.Kind == "click")
            .GroupBy(e => new { e.Kind, e.Name, e.Screen })
            .Select(g => new { kind = g.Key.Kind, name = g.Key.Name, screen = g.Key.Screen, count = g.Count(),
                users = g.Select(e => e.UserExternalId).Distinct().Count() })
            .OrderByDescending(x => x.count).Take(60)
            .ToListAsync(cancellationToken);

        var errors = await scoped.Where(e => e.Kind == "error" || e.Kind == "slow")
            .GroupBy(e => new { e.Kind, e.Name, e.Status })
            .Select(g => new { kind = g.Key.Kind, name = g.Key.Name, status = g.Key.Status, count = g.Count(),
                avgDurationMs = g.Average(e => (double?)e.DurationMs), users = g.Select(e => e.UserExternalId).Distinct().Count() })
            .OrderByDescending(x => x.count).Take(30)
            .ToListAsync(cancellationToken);

        var (logins, loginsAvailable) = await LoginRowsAsync(start, end, user, cancellationToken);

        // Dias no fuso do navegador, todos presentes (zeros inclusive).
        DateTime Local(DateTimeOffset at) => at.UtcDateTime + offset;
        var firstDay = Local(start).Date;
        var lastDay = Local(end).Date;
        var dayIndex = new Dictionary<DateTime, DayAcc>();
        for (var d = firstDay; d <= lastDay; d = d.AddDays(1)) dayIndex[d] = new DayAcc();
        var heat = new int[7, 24];
        var heatEvents = new int[7, 24];
        foreach (var h in hourly)
        {
            var local = Local(h.Hour);
            if (dayIndex.TryGetValue(local.Date, out var acc))
            {
                if (h.User is { } id) acc.Users.Add(id);
                acc.Views += h.Views; acc.Actions += h.Actions; acc.Clicks += h.Clicks; acc.Errors += h.Errors; acc.Events += h.Total;
            }
            heat[(int)local.DayOfWeek, local.Hour]++;
            heatEvents[(int)local.DayOfWeek, local.Hour] += h.Total;
        }
        foreach (var s in sessions)
            if (dayIndex.TryGetValue(Local(s.StartedAt).Date, out var acc)) acc.Sessions++;
        foreach (var l in logins)
            if (dayIndex.TryGetValue(Local(new DateTimeOffset(AsUtc(l.At))).Date, out var acc))
            {
                if (l.Success) acc.Logins++; else acc.FailedLogins++;
            }

        var names = sessions.Where(x => x.User is not null).GroupBy(x => x.User!.Value).ToDictionary(g => g.Key, g => g.First().UserName);
        foreach (var l in logins.Where(l => l.UserExternalId is not null))
            names.TryAdd(l.UserExternalId!.Value, l.UserName);
        var perUserEvents = hourly.Where(h => h.User is not null).GroupBy(h => h.User!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var perUserSessions = sessions.Where(x => x.User is not null).GroupBy(x => x.User!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var perUserLogins = logins.Where(l => l.UserExternalId is not null).GroupBy(l => l.UserExternalId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var userIds = perUserEvents.Keys.Union(perUserSessions.Keys).Union(perUserLogins.Keys).ToList();
        var users = userIds.Select(id =>
        {
            var ev = perUserEvents.GetValueOrDefault(id) ?? [];
            var ss = perUserSessions.GetValueOrDefault(id) ?? [];
            var lg = perUserLogins.GetValueOrDefault(id) ?? [];
            return new
            {
                userExternalId = id,
                userName = names.GetValueOrDefault(id) ?? "",
                sessions = ss.Count,
                logins = lg.Count(l => l.Success),
                failedLogins = lg.Count(l => !l.Success),
                foregroundMs = ss.Sum(x => x.ForegroundMs),
                activeDays = ev.Select(h => Local(h.Hour).Date).Distinct().Count(),
                views = ev.Sum(h => h.Views),
                actions = ev.Sum(h => h.Actions),
                clicks = ev.Sum(h => h.Clicks),
                errors = ev.Sum(h => h.Errors),
                lastSeenAt = ss.Count > 0 ? ss.Max(x => x.LastSeenAt) : (DateTimeOffset?)null
            };
        }).OrderByDescending(x => x.foregroundMs).ThenByDescending(x => x.views).ToList();

        var activeUsers = hourly.Where(h => h.User is not null).Select(h => h.User).Concat(sessions.Select(x => x.User))
            .Where(x => x is not null).Distinct().Count();
        var durations = sessions.Select(x => Math.Min((x.LastSeenAt - x.StartedAt).TotalMinutes, 24 * 60)).ToList();

        return Ok(new
        {
            from = start,
            to = end,
            loginsAvailable,
            kpis = new
            {
                activeUsers,
                sessions = sessions.Count,
                logins = logins.Count(l => l.Success),
                failedLogins = logins.Count(l => !l.Success),
                avgSessionMinutes = durations.Count > 0 ? Math.Round(durations.Average(), 1) : 0,
                foregroundMs = sessions.Sum(x => x.ForegroundMs),
                backgroundMs = sessions.Sum(x => x.BackgroundMs),
                awayMs = sessions.Sum(x => x.AwayMs),
                views = hourly.Sum(h => h.Views),
                actions = hourly.Sum(h => h.Actions),
                clicks = hourly.Sum(h => h.Clicks),
                errors = hourly.Sum(h => h.Errors),
                events = hourly.Sum(h => h.Total)
            },
            days = dayIndex.OrderBy(d => d.Key).Select(d => new
            {
                day = d.Key.ToString("yyyy-MM-dd"),
                activeUsers = d.Value.Users.Count,
                sessions = d.Value.Sessions,
                logins = d.Value.Logins,
                failedLogins = d.Value.FailedLogins,
                views = d.Value.Views,
                actions = d.Value.Actions,
                clicks = d.Value.Clicks,
                errors = d.Value.Errors,
                events = d.Value.Events
            }),
            // Dia da semana (0 = domingo) × hora: count = usuários ativos (soma das horas em que cada usuário teve
            // atividade naquele horário ao longo do período); events = eventos.
            heatmap = Enumerable.Range(0, 7).SelectMany(w => Enumerable.Range(0, 24)
                    .Select(h => new { weekday = w, hour = h, count = heat[w, h], users = heat[w, h], events = heatEvents[w, h] }))
                .Where(x => x.count > 0),
            screens,
            actions,
            errors,
            clients = new
            {
                browsers = Breakdown(sessions, x => x.Browser),
                os = Breakdown(sessions, x => x.Os),
                devices = Breakdown(sessions, x => x.Device)
            },
            users
        });
    }

    /// <summary>Tentativas de login (sucesso e falha), mais recentes primeiro.</summary>
    [HttpGet("logins")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Logins([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? user,
        [FromQuery] bool? success, [FromQuery] string? search, [FromQuery] int page = 0, [FromQuery] int size = 50,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = Range(from, to);
        (page, size) = Paging(page, size);
        try
        {
            var query = LoginsQuery(start, end, user);
            if (success is { } ok) query = query.Where(l => l.Success == ok);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(l => l.UserName.ToLower().Contains(term) || (l.Ip != null && l.Ip.Contains(term)));
            }
            var total = await query.CountAsync(cancellationToken);
            var rows = await query.OrderByDescending(l => l.At).Skip(page * size).Take(size).ToListAsync(cancellationToken);
            var items = rows.Select(l => new { l.Id, at = AsUtc(l.At), l.UserExternalId, l.UserName, l.Success, l.Reason, l.Ip, l.UserAgent });
            return Ok(new { items, total, page, size });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // auth.LoginEvents ainda não existe (a migração da auth não rodou): lista vazia, não erro.
            logger.LogWarning(e, "Não li auth.LoginEvents");
            return Ok(new { items = Array.Empty<object>(), total = 0, page, size, unavailable = true });
        }
    }

    /// <summary>Sessões (abas) que tocaram o período, mais recentes primeiro.</summary>
    [HttpGet("sessions")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Sessions([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? user,
        [FromQuery] string? search, [FromQuery] int page = 0, [FromQuery] int size = 50, CancellationToken cancellationToken = default)
    {
        var (start, end) = Range(from, to);
        (page, size) = Paging(page, size);
        var query = db.UsageSessions.AsNoTracking().Where(x => x.StartedAt <= end && x.LastSeenAt >= start);
        if (user is { } u) query = query.Where(x => x.UserExternalId == u);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.UserName.ToLower().Contains(term) || (x.Ip != null && x.Ip.Contains(term))
                || (x.Browser != null && x.Browser.ToLower().Contains(term)) || (x.Os != null && x.Os.ToLower().Contains(term)));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.LastSeenAt).Skip(page * size).Take(size).ToListAsync(cancellationToken);
        return Ok(new { items = items.Select(SessionView), total, page, size });
    }

    /// <summary>Eventos (o caminho do usuário), mais recentes primeiro; filtros por usuário, sessão, tipo, tela e texto.</summary>
    [HttpGet("events")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Events([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? user,
        [FromQuery] Guid? session, [FromQuery] string? kind, [FromQuery] string? screen, [FromQuery] string? search,
        [FromQuery] int page = 0, [FromQuery] int size = 50, CancellationToken cancellationToken = default)
    {
        (page, size) = Paging(page, size);
        var query = db.UsageEvents.AsNoTracking();
        if (session is { } sid)
            query = query.Where(e => e.SessionId == sid);
        else
        {
            var (start, end) = Range(from, to);
            query = query.Where(e => e.At >= start && e.At <= end);
        }
        if (user is { } u) query = query.Where(e => e.UserExternalId == u);
        var kinds = (kind ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(UsageEvent.Kinds.Contains).ToList();
        if (kinds.Count > 0) query = query.Where(e => kinds.Contains(e.Kind));
        if (!string.IsNullOrWhiteSpace(screen)) query = query.Where(e => e.Screen == screen);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e => (e.Name != null && e.Name.ToLower().Contains(term)) || (e.Detail != null && e.Detail.ToLower().Contains(term))
                || (e.Route != null && e.Route.ToLower().Contains(term)) || (e.Card != null && e.Card.Contains(term))
                || e.UserName.ToLower().Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Skip(page * size).Take(size)
            .Select(e => new { e.Id, e.At, e.SessionId, e.UserExternalId, e.UserName, e.Kind, e.Screen, e.Route, e.Card, e.Name, e.Detail, e.DurationMs, e.Status })
            .ToListAsync(cancellationToken);
        return Ok(new { items, total, page, size });
    }

    /// <summary>Último acesso de cada usuário (para os cartões da gestão mostrarem "visto há…" de quem está offline).</summary>
    [HttpGet("last-seen")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> LastSeen(CancellationToken cancellationToken)
    {
        var seen = await db.UsageSessions.AsNoTracking().Where(x => x.UserExternalId != null)
            .GroupBy(x => x.UserExternalId)
            .Select(g => g.OrderByDescending(x => x.LastSeenAt)
                .Select(x => new { x.UserExternalId, x.LastSeenAt, x.LastScreen, x.Ip }).First())
            .ToListAsync(cancellationToken);
        var logins = new Dictionary<Guid, DateTime>();
        try
        {
            logins = await auth.LoginEvents.AsNoTracking().Where(l => l.Success && l.UserExternalId != null)
                .GroupBy(l => l.UserExternalId!.Value)
                .Select(g => new { g.Key, At = g.Max(l => l.At) })
                .ToDictionaryAsync(x => x.Key, x => x.At, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Não li auth.LoginEvents");
        }
        var ids = seen.Select(x => x.UserExternalId!.Value).Union(logins.Keys);
        return Ok(ids.Select(id =>
        {
            var s = seen.FirstOrDefault(x => x.UserExternalId == id);
            return new
            {
                userExternalId = id,
                lastSeenAt = s?.LastSeenAt,
                lastScreen = s?.LastScreen,
                lastIp = s?.Ip,
                lastLoginAt = logins.TryGetValue(id, out var at) ? AsUtc(at) : (DateTime?)null
            };
        }));
    }

    /// <summary>Histórico de um usuário (30 dias): últimos logins, sessões e o caminho recente, com totais.</summary>
    [HttpGet("users/{externalId:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UserDetail(Guid externalId, [FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, MaxRangeDays));
        var sessions = await db.UsageSessions.AsNoTracking().Where(x => x.UserExternalId == externalId && x.LastSeenAt >= since)
            .OrderByDescending(x => x.LastSeenAt).Take(20).ToListAsync(cancellationToken);
        var events = await db.UsageEvents.AsNoTracking().Where(e => e.UserExternalId == externalId && e.At >= since)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(150)
            .Select(e => new { e.Id, e.At, e.SessionId, e.Kind, e.Screen, e.Route, e.Card, e.Name, e.Detail, e.DurationMs, e.Status })
            .ToListAsync(cancellationToken);
        var totals = await db.UsageSessions.AsNoTracking().Where(x => x.UserExternalId == externalId && x.LastSeenAt >= since)
            .GroupBy(_ => 1)
            .Select(g => new { sessions = g.Count(), foregroundMs = g.Sum(x => x.ForegroundMs), views = g.Sum(x => x.Views), actions = g.Sum(x => x.Actions) })
            .FirstOrDefaultAsync(cancellationToken);
        var errors = await db.UsageEvents.AsNoTracking()
            .CountAsync(e => e.UserExternalId == externalId && e.At >= since && e.Kind == "error", cancellationToken);
        object logins = Array.Empty<object>();
        int loginCount = 0, failedLoginCount = 0;
        try
        {
            var sinceAuth = since.UtcDateTime;
            var counts = await auth.LoginEvents.AsNoTracking().Where(l => l.UserExternalId == externalId && l.At >= sinceAuth)
                .GroupBy(l => l.Success).Select(g => new { g.Key, n = g.Count() }).ToListAsync(cancellationToken);
            loginCount = counts.FirstOrDefault(c => c.Key)?.n ?? 0;
            failedLoginCount = counts.FirstOrDefault(c => !c.Key)?.n ?? 0;
            var rows = await auth.LoginEvents.AsNoTracking().Where(l => l.UserExternalId == externalId && l.At >= sinceAuth)
                .OrderByDescending(l => l.At).Take(20).ToListAsync(cancellationToken);
            logins = rows.Select(l => new { l.Id, at = AsUtc(l.At), l.Success, l.Reason, l.Ip, l.UserAgent }).ToList();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Não li auth.LoginEvents");
        }
        return Ok(new
        {
            logins,
            sessions = sessions.Select(SessionView),
            events,
            totals = new
            {
                sessions = totals?.sessions ?? 0,
                foregroundMs = totals?.foregroundMs ?? 0L,
                views = totals?.views ?? 0,
                actions = totals?.actions ?? 0,
                errors,
                logins = loginCount,
                failedLogins = failedLoginCount
            }
        });
    }

    // ---------------------------------------------------------------- apoio

    private IQueryable<LoginEventRecord> LoginsQuery(DateTimeOffset start, DateTimeOffset end, Guid? user)
    {
        // auth.LoginEvents guarda DateTime.Now do servidor da auth — UTC no Cloud Run — sem fuso.
        var s = start.UtcDateTime;
        var e = end.UtcDateTime;
        var query = auth.LoginEvents.AsNoTracking().Where(l => l.At >= s && l.At <= e);
        if (user is { } u) query = query.Where(l => l.UserExternalId == u);
        return query;
    }

    private async Task<(List<LoginRow> Rows, bool Available)> LoginRowsAsync(DateTimeOffset start, DateTimeOffset end, Guid? user, CancellationToken ct)
    {
        try
        {
            var rows = await LoginsQuery(start, end, user).Select(l => new LoginRow(l.At, l.Success, l.UserExternalId, l.UserName)).ToListAsync(ct);
            return (rows, true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Não li auth.LoginEvents");
            return ([], false);
        }
    }

    private static object SessionView(UsageSession x) => new
    {
        x.Id, x.UserExternalId, x.UserName, x.StartedAt, x.LastSeenAt, x.EndedAt, x.Ip, x.UserAgent, x.Browser, x.Os, x.Device,
        x.ScreenSize, x.Language, x.TimeZone, x.AppVersion, x.LastScreen, x.LastRoute, x.ForegroundMs, x.BackgroundMs, x.AwayMs,
        x.Views, x.Actions, x.Events
    };

    private static IEnumerable<object> Breakdown(List<SessionRow> sessions, Func<SessionRow, string?> key) =>
        sessions.GroupBy(x => string.IsNullOrWhiteSpace(key(x)) ? "Desconhecido" : key(x)!)
            .Select(g => new { name = g.Key, sessions = g.Count(), users = g.Select(x => x.User).Where(u => u is not null).Distinct().Count() })
            .OrderByDescending(x => x.sessions).Take(12);

    private static (DateTimeOffset Start, DateTimeOffset End) Range(DateTimeOffset? from, DateTimeOffset? to)
    {
        var end = (to ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var start = (from ?? end.AddDays(-7)).ToUniversalTime();
        if (start > end) (start, end) = (end, start);
        if (end - start > TimeSpan.FromDays(MaxRangeDays)) start = end.AddDays(-MaxRangeDays);
        return (start, end);
    }

    /// <summary>auth.LoginEvents não tem fuso (DateTime.Now do servidor da auth = UTC no Cloud Run).</summary>
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static (int Page, int Size) Paging(int page, int size) => (Math.Max(0, page), Math.Clamp(size, 1, 200));

    /// <summary>timestamptz no Npgsql só aceita offset zero: tudo vira UTC, dentro do intervalo aceito.</summary>
    private static DateTimeOffset? Clamp(DateTimeOffset? value, DateTimeOffset min, DateTimeOffset max)
    {
        if (value is not { } v) return null;
        v = v.ToUniversalTime();
        return v < min ? min : v > max ? max : v;
    }

    private static Guid DerivedSessionId(Guid sessionId, Guid? userId)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{sessionId:N}:{userId:N}"));
        return new Guid(bytes);
    }

    private static (Guid? Id, string Name) CurrentUser(ClaimsPrincipal user)
    {
        Guid? id = Guid.TryParse(user.FindFirst("ExternalId")?.Value, out var g) && g != Guid.Empty ? g : null;
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst("name")?.Value ?? user.FindFirst("unique_name")?.Value;
        return (id, string.IsNullOrWhiteSpace(name) ? "desconhecido" : name);
    }

    private sealed record HourRow(DateTime Day, int HourOfDay, Guid? User, int Total, int Views, int Actions, int Clicks, int Errors)
    {
        /// <summary>Início da hora em UTC (o banco agrupa por dia/hora em UTC).</summary>
        public DateTimeOffset Hour => new(DateTime.SpecifyKind(Day.Date.AddHours(HourOfDay), DateTimeKind.Utc));
    }
    private sealed record SessionRow(Guid? User, string UserName, DateTimeOffset StartedAt, DateTimeOffset LastSeenAt, long ForegroundMs,
        long BackgroundMs, long AwayMs, string? Browser, string? Os, string? Device);
    private sealed record LoginRow(DateTime At, bool Success, Guid? UserExternalId, string UserName);

    private sealed class DayAcc
    {
        public HashSet<Guid> Users { get; } = [];
        public int Sessions, Logins, FailedLogins, Views, Actions, Clicks, Errors, Events;
    }
}

public record UsageClientDto(string? Browser, string? Os, string? Device, string? ScreenSize, string? Language, string? TimeZone, string? AppVersion);

public record UsageSessionDto(Guid Id, DateTimeOffset? StartedAt, string? Screen, string? Route, long ForegroundMs, long BackgroundMs,
    long AwayMs, bool Ended, UsageClientDto? Client);

public record UsageEventDto(DateTimeOffset? At, string? Kind, string? Screen, string? Route, string? Card, string? Name, string? Detail,
    int? DurationMs, int? Status);

public record UsageBatchRequest(UsageSessionDto? Session, List<UsageEventDto>? Events);
