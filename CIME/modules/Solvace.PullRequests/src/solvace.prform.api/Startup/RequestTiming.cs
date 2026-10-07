using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace solvace.prform.api.Startup;

/// <summary>
/// Tempo de cada requisição (0070): cabeçalho <c>Server-Timing</c> (total e banco, com o nº de comandos SQL — visível
/// no DevTools) e log das requisições acima de <c>Performance:SlowRequestMs</c> (padrão 300 ms) com rota, status,
/// comandos e bytes. Os comandos vêm dos eventos de diagnóstico do EF Core, de todos os contextos, sem mexer neles.
/// </summary>
public sealed class RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger, IConfiguration configuration)
{
    private readonly int _slowMs = configuration.GetValue("Performance:SlowRequestMs", 300);

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }
        var stats = new RequestDbStats();
        RequestDbStats.Current.Value = stats;
        var watch = Stopwatch.StartNew();
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["Server-Timing"] = $"app;dur={watch.Elapsed.TotalMilliseconds:0}, db;dur={stats.DbMilliseconds:0};desc=\"{stats.Commands} SQL\"";
            headers["Timing-Allow-Origin"] = "*";
            return Task.CompletedTask;
        });
        try
        {
            await next(context);
        }
        finally
        {
            watch.Stop();
            RequestDbStats.Current.Value = null;
            if (watch.ElapsedMilliseconds >= _slowMs)
                logger.LogWarning("Requisição lenta: {Method} {Path} {Status} em {Elapsed} ms — {Commands} comandos SQL ({DbMs} ms), {Bytes} bytes",
                    context.Request.Method, context.Request.Path.Value, context.Response.StatusCode, watch.ElapsedMilliseconds,
                    stats.Commands, (long)stats.DbMilliseconds, context.Response.ContentLength);
        }
    }
}

/// <summary>Comandos SQL da requisição atual (fluem pelo <see cref="AsyncLocal{T}"/> do middleware).</summary>
public sealed class RequestDbStats
{
    public static readonly AsyncLocal<RequestDbStats?> Current = new();
    private int _commands;
    private long _ticks;

    public int Commands => _commands;
    public double DbMilliseconds => TimeSpan.FromTicks(Interlocked.Read(ref _ticks)).TotalMilliseconds;

    public void Add(TimeSpan duration)
    {
        Interlocked.Increment(ref _commands);
        Interlocked.Add(ref _ticks, duration.Ticks);
    }
}

/// <summary>Assina os eventos do EF Core (todos os DbContexts) e soma os comandos na requisição atual.</summary>
public sealed class EfCommandObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private static int _started;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) DiagnosticListener.AllListeners.Subscribe(new EfCommandObserver());
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == Microsoft.EntityFrameworkCore.DbLoggerCategory.Name) listener.Subscribe(this, name => name == RelationalEventId.CommandExecuted.Name
                                                                                     || name == RelationalEventId.CommandError.Name);
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (RequestDbStats.Current.Value is { } stats && value.Value is CommandEndEventData data) stats.Add(data.Duration);
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }
}
