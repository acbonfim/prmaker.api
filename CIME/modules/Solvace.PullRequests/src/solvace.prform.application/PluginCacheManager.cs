using Cime.BuildingBlocks.Cache;
using Microsoft.Extensions.DependencyInjection;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;

namespace solvace.prform.application;

public interface IPluginCacheManager
{
    Task LoadPluginsAsync(CancellationToken cancellationToken = default);
    Task RefreshPluginsAsync(CancellationToken cancellationToken = default);
    IEnumerable<Plugin>? GetCachedPlugins();
    Plugin? GetCachedPluginById(int id);
    Plugin? GetCachedPluginByName(string name);
    string? GetPluginConfigurationValue(int pluginId, string key);
    IDictionary<string, string>? GetAllPluginConfigurations(int pluginId);
    bool HasPluginConfiguration(int pluginId, string key);
}

public class PluginCacheManager : IPluginCacheManager
{
    private const string PluginsCacheKey = "plugins:all";
    private const int CacheExpirationMinutes = 1440; // 24 horas
    
    private readonly ICacheService _cacheService;
    private readonly IServiceScopeFactory _scopeFactory;

    public PluginCacheManager(ICacheService cacheService, IServiceScopeFactory scopeFactory)
    {
        _cacheService = cacheService;
        _scopeFactory = scopeFactory;
    }

    public async Task LoadPluginsAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheService.Exists(PluginsCacheKey))
            return;

        await RefreshPluginsAsync(cancellationToken);
    }

    public async Task RefreshPluginsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var pluginApplication = scope.ServiceProvider.GetRequiredService<IPluginApplication>();
        
        var plugins = await pluginApplication.GetPlugins(cancellationToken);
        var pluginsList = plugins.ToList();
        
        _cacheService.Set(PluginsCacheKey, pluginsList, CacheExpirationMinutes);
    }

    public IEnumerable<Plugin>? GetCachedPlugins()
    {
        return _cacheService.Get<List<Plugin>>(PluginsCacheKey);
    }

    public Plugin? GetCachedPluginById(int id)
    {
        var plugins = GetCachedPlugins();
        return plugins?.First(p => p.Id == id);
    }
    
    public Plugin? GetCachedPluginByName(string name)
    {
        var plugins = GetCachedPlugins();
        return plugins?.First(p => p.Description.ToLower() == name.ToLower());
    }
    
    /// <summary>
    /// Obtém o valor de uma configuração específica de um plugin
    /// </summary>
    public string? GetPluginConfigurationValue(int pluginId, string key)
    {
        var plugin = GetCachedPluginById(pluginId);
        return plugin?.Configurations.GetConfigurationValue(key);
    }

    /// <summary>
    /// Obtém todas as configurações de um plugin como dicionário
    /// </summary>
    public IDictionary<string, string>? GetAllPluginConfigurations(int pluginId)
    {
        var plugin = GetCachedPluginById(pluginId);
        return plugin?.Configurations.GetAllConfigurations();
    }

    /// <summary>
    /// Verifica se um plugin possui uma configuração específica
    /// </summary>
    public bool HasPluginConfiguration(int pluginId, string key)
    {
        var plugin = GetCachedPluginById(pluginId);
        return plugin?.Configurations.HasConfiguration(key) ?? false;
    }
}