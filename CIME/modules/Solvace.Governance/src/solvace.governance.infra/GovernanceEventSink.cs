using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using solvace.governance.application.Contracts;
using solvace.governance.domain;
using solvace.governance.domain.Engine;
using solvace.governance.domain.Entities;
using solvace.governance.infra.Contexts;

namespace solvace.governance.infra;

/// <summary>
/// 0098: fila da trilha de governança. A requisição só enfileira (nada de ida ao banco no caminho do usuário); o
/// <see cref="GovernanceEventWriter"/> grava em lote. Fila cheia descarta o mais antigo (a API vale mais que o log).
/// </summary>
public sealed class GovernanceEventSink : IGovernanceEventSink
{
    public const int Capacity = 5_000;

    private readonly Channel<GovernanceEventData> _channel = Channel.CreateBounded<GovernanceEventData>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<GovernanceEventData> Reader => _channel.Reader;

    public void Enqueue(GovernanceEventData data) => _channel.Writer.TryWrite(data);
}

/// <summary>
/// Grava os eventos em lote (até 200 ou a cada 2 s), aplica o modo/origens das configurações, roda os gatilhos do
/// servidor (server:*) e apaga uma vez por dia o que passou da retenção.
/// </summary>
public sealed class GovernanceEventWriter(
    GovernanceEventSink sink,
    IServiceScopeFactory scopes,
    IGovernancePolicyProvider policy,
    TimeProvider clock,
    ILogger<GovernanceEventWriter> logger) : BackgroundService
{
    private const int BatchSize = 200;
    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(2);
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<GovernanceEventData>(BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await sink.Reader.WaitToReadAsync(stoppingToken)) break;
                using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                window.CancelAfter(FlushEvery);
                try
                {
                    while (batch.Count < BatchSize)
                    {
                        while (batch.Count < BatchSize && sink.Reader.TryRead(out var item)) batch.Add(item);
                        if (batch.Count >= BatchSize) break;
                        if (!await sink.Reader.WaitToReadAsync(window.Token)) break;
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Janela de 2 s acabou: grava o que juntou.
                }
                await FlushAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Governança: falha ao gravar {Count} eventos (descartados).", batch.Count);
            }
            finally
            {
                batch.Clear();
            }
        }

        // Desligando: grava o que sobrou na fila (sem esperar mais).
        while (sink.Reader.TryRead(out var rest)) batch.Add(rest);
        if (batch.Count > 0)
        {
            try { await FlushAsync(batch, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Governança: eventos perdidos no desligamento."); }
        }
    }

    private async Task FlushAsync(List<GovernanceEventData> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;
        var p = await policy.GetAsync(ct);
        if (p.Settings.Mode == GovernanceMode.Off) return;
        var sources = p.Settings.RecordSources.Count == 0 ? GovernanceSource.All : [.. p.Settings.RecordSources];
        // Mudança na própria governança sempre fica registrada (transparência para o admin).
        var events = batch
            .Where(d => d.Category == "governance" || sources.Contains(d.Source))
            .Select(d => new GovernanceEvent(d))
            .ToList();
        if (events.Count == 0) return;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceContext>();
        db.Events.AddRange(events);
        await db.SaveChangesAsync(ct);

        await RunServerHooksAsync(scope.ServiceProvider, p.Hooks, events, ct);
        await PurgeIfDueAsync(db, p.Settings.RetentionDays, ct);
    }

    private async Task RunServerHooksAsync(IServiceProvider sp, IReadOnlyList<GovernanceHook> hooks, List<GovernanceEvent> events, CancellationToken ct)
    {
        var server = hooks.Where(h => h.Enabled && h.Event.StartsWith(GovernanceHookEvent.ServerPrefix)).ToList();
        if (server.Count == 0) return;
        var runner = sp.GetService<IGovernanceServerHookRunner>();
        if (runner is null) return;
        foreach (var evt in events)
        {
            foreach (var hook in server.Where(h => ServerHookMatches(h, evt)))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await runner.RunAsync(hook, evt, timeout.Token);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Governança: hook {Hook} falhou no evento {Event}.", hook.Key, evt.Id);
                }
            }
        }
    }

    /// <summary>server:&lt;glob da ação&gt; + matcher (glob da categoria ou do resultado; vazio = tudo).</summary>
    public static bool ServerHookMatches(GovernanceHook hook, GovernanceEvent evt)
    {
        // Um gatilho nunca dispara sobre a própria mudança de configuração da governança (evita laço com timeline/webhook).
        if (evt.Category == "governance") return false;
        var actionGlob = hook.Event[GovernanceHookEvent.ServerPrefix.Length..];
        if (!GovernanceRuleEngine.Glob(actionGlob, evt.Action)) return false;
        return string.IsNullOrWhiteSpace(hook.Matcher)
               || GovernanceRuleEngine.Glob(hook.Matcher, evt.Category)
               || GovernanceRuleEngine.Glob(hook.Matcher, evt.Outcome);
    }

    private async Task PurgeIfDueAsync(GovernanceContext db, int retentionDays, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (now - _lastPurge < TimeSpan.FromDays(1)) return;
        _lastPurge = now;
        try
        {
            var limit = now.AddDays(-Math.Max(30, retentionDays));
            var removed = await new Repositories.GovernanceRepository(db).PurgeEventsAsync(limit, ct);
            if (removed > 0) logger.LogInformation("Governança: {Count} eventos anteriores a {Limit:yyyy-MM-dd} apagados.", removed, limit);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Governança: falha na limpeza da retenção.");
        }
    }
}
