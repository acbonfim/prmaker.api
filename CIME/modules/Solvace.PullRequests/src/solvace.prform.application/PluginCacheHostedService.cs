using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace solvace.prform.application;

public class PluginCacheHostedService : IHostedService
{
    private readonly IPluginCacheManager _pluginCacheManager;
    private readonly ILogger<PluginCacheHostedService> _logger;

    public PluginCacheHostedService(
        IPluginCacheManager pluginCacheManager,
        ILogger<PluginCacheHostedService> logger)
    {
        _pluginCacheManager = pluginCacheManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Carregando cache de plugins na inicialização...");
            await _pluginCacheManager.LoadPluginsAsync(cancellationToken);
            _logger.LogInformation("Cache de plugins carregado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar cache de plugins na inicialização.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Finalizando serviço de cache de plugins.");
        return Task.CompletedTask;
    }
}