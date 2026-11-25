using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;

namespace solvace.prform.Knowledge;

/// <summary>
/// Monta o índice da engenharia reversa em memória antes de alguém perguntar: na instância nova (deploy, escala) a primeira
/// busca carregava ~60 mil itens e levava ~25 s (2026-11-25: a pergunta da Base logo depois do deploy esperou 25 s numa busca).
/// Roda ao subir, a cada <see cref="Every"/> (publicação nova muda a marca e recarrega) e quando a tela da Base abre. Uma carga
/// por vez, no próprio escopo — quem precisa espera por ela em vez de começar outra (ou cancelar no meio).
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
                logger.LogInformation("Índice da engenharia reversa carregado em {Ms} ms", clock.ElapsedMilliseconds);
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
