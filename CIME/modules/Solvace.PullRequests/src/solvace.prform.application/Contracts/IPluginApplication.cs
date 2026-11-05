using solvace.prform.domain.Entities;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.application.Contracts;

public interface IPluginApplication
{
    Task<Plugin> GetPluginById(int pluginId, CancellationToken cancellationToken);
    Task<PluginRespose> Create(PluginRequest request, CancellationToken cancellationToken);
    Task<PluginRespose> UpdateConfiguration(int id, PluginRequest request, CancellationToken cancellationToken);
    Task<IEnumerable<Plugin>> GetPlugins(CancellationToken cancellationToken);
    Task DeletePlugin(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Avisa em tempo real quem está na tela de PR que a configuração mudou — só para o plugin de Pull Request (9);
    /// best-effort (0086: também usado pelo PATCH das configurações).
    /// </summary>
    Task NotifyPullRequestConfigUpdatedAsync(int pluginId, CancellationToken cancellationToken);
}