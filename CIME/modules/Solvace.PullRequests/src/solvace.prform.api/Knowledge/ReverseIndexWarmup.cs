using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;

namespace solvace.prform.Knowledge;

/// <summary>
/// Deixa a busca da engenharia reversa pronta antes de alguém perguntar. Até a 0073 isso era montar ~60 mil itens em memória
/// (25–238 s numa instância nova em produção); com a busca no banco (0073) só o glossário (sinônimos) fica em cache — carga
/// curta, mas ainda é a primeira consulta de cada instância. Roda ao subir, a cada <see cref="Every"/> (publicação nova muda a
/// marca e recarrega) e quando a tela da Base abre. Uma carga por vez, no próprio escopo.
/// </summary>
public sealed class ReverseIndexWarmup(IServiceScopeFactory scopes, ILogger<ReverseIndexWarmup> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private Task? _current;

    /// <summary>A carga em andamento (ou uma nova); já carregado e em dia, termina na hora (só confere a marca).</summary>
    public Task WarmAsync()
    {
        lock (_gate)
        {
            if (_current is null || _current.IsCompleted) _current = Task.Run(RunAsync);
            return _current;
        }
    }

    /// <summary>Espera o índice pronto por no máximo <paramref name="budget"/>; true = pronto.</summary>
    public async Task<bool> WaitAsync(TimeSpan budget, CancellationToken ct)
    {
        var warm = WarmAsync();
        return await Task.WhenAny(warm, Task.Delay(budget, ct)) == warm && warm.IsCompletedSuccessfully;
    }

    private async Task RunAsync()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var scope = scopes.CreateScope();
            await ReverseSearch.SynonymsAsync(scope.ServiceProvider.GetRequiredService<IKnowledgeRepository>(), CancellationToken.None);
            if (clock.ElapsedMilliseconds > 1_000)
                logger.LogInformation("Glossário da busca da engenharia reversa carregado em {Ms} ms", clock.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Não consegui carregar o índice da engenharia reversa");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await WarmAsync();
                await Task.Delay(Every, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // desligando
        }
    }
}
