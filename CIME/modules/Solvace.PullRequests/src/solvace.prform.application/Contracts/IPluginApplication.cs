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
}