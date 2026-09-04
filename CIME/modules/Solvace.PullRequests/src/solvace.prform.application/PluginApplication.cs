using Cime.BuildingBlocks.RealTime;
using Microsoft.EntityFrameworkCore;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Enums;
using solvace.prform.domain.RealTime;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.application;

public class PluginApplication : IPluginApplication
{
    private readonly DefaultContext _context;
    private readonly IPluginCacheManager _pluginCacheManager;
    private readonly IRealTimeNotifier _realTimeNotifier;
    private DbSet<Plugin> _repository;

    public PluginApplication(DefaultContext context, IPluginCacheManager pluginCacheManager, IRealTimeNotifier realTimeNotifier)
    {
        _context = context;
        _pluginCacheManager = pluginCacheManager;
        _realTimeNotifier = realTimeNotifier;
        _repository = context.Plugins;
    }
    
    public async Task<bool> CommitAsync(CancellationToken cancellationToken) => await _context.SaveChangesAsync(cancellationToken) > 0;
    
    public async Task<IEnumerable<Plugin>> GetPlugins(CancellationToken cancellationToken) => 
        await _repository
            .Include(x => x.Configurations)
            .Where(x => !x.IsDeleted)
            .ToListAsync(cancellationToken);
    
    public async Task<Plugin> GetPluginById(int pluginId,CancellationToken cancellationToken) => 
        await _repository
            .Include(x => x.Configurations)
            .FirstAsync(x => x.Id == pluginId,cancellationToken);

    public async Task<PluginRespose> Create(PluginRequest request, CancellationToken cancellationToken)
    {
        var requestRegister = request.Create(request);

        var response = await _repository.AddAsync(requestRegister, cancellationToken);

        if (await CommitAsync(cancellationToken))
        {
            await _pluginCacheManager.RefreshPluginsAsync(cancellationToken);
            await NotifyPullRequestConfigUpdatedAsync(response.Entity.Id, cancellationToken);
            return new PluginRespose(){Id = response.Entity.Id};
        }



        throw new Exception("Error on save");
    }
    
    public async Task<PluginRespose> UpdateConfiguration(int id, PluginRequest request, CancellationToken cancellationToken)
    {
        var plugin = await GetPluginById(id, cancellationToken);

        plugin.SetConfigurations(request.Configurations);
        plugin.SetAdminOnly(request.AdminOnly);

        if (await CommitAsync(cancellationToken))
        {
            await _pluginCacheManager.RefreshPluginsAsync(cancellationToken);
            await NotifyPullRequestConfigUpdatedAsync(plugin.Id, cancellationToken);
            return new PluginRespose(){Id = plugin.Id};
        }

        throw new Exception("Error on update");
    }

    /// <summary>
    /// Avisa quem está na tela de PR que a configuração de repositórios/branches mudou.
    /// Só dispara para o plugin de Pull Request. Best-effort: nunca quebra a operação.
    /// </summary>
    private async Task NotifyPullRequestConfigUpdatedAsync(int pluginId, CancellationToken cancellationToken)
    {
        if (pluginId != EPlugin.PULLREQUEST)
            return;

        try
        {
            await _realTimeNotifier.NotifyGroupAsync(
                PullRequestRealTimeEvents.Group,
                PullRequestRealTimeEvents.EventConfigUpdated,
                new { pluginId, section = "all" },
                cancellationToken);
        }
        catch
        {
            // Notificação em tempo real é best-effort.
        }
    }
    
    public async Task DeletePlugin(int id, CancellationToken cancellationToken)
    {
        var plugin = await GetPluginById(id, cancellationToken);

        plugin.Delete();

        if (await CommitAsync(cancellationToken))
        {
            await _pluginCacheManager.RefreshPluginsAsync(cancellationToken);
            return; 
        }
        
        throw new Exception("Error on delete");
    }
}