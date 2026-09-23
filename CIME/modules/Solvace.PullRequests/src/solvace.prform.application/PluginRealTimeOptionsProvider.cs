using Cime.BuildingBlocks.RealTime;
using Microsoft.Extensions.Options;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;

namespace solvace.prform.application;

/// <summary>
/// Resolve as opções de tempo real a partir do plugin "Realtime Configurations" (cache de plugins),
/// com fallback para as configurações de ambiente (appsettings) quando o plugin ou uma chave não
/// existir. Segue a mesma estrutura usada pelos módulos AI/Azure para ler configuração de plugin.
/// </summary>
public class PluginRealTimeOptionsProvider : IRealTimeOptionsProvider
{
    private const string PluginName = "Realtime Configurations";

    private readonly IPluginCacheManager _pluginCacheManager;
    private readonly RealTimeOptions _envOptions;

    public PluginRealTimeOptionsProvider(IPluginCacheManager pluginCacheManager, IOptions<RealTimeOptions> envOptions)
    {
        _pluginCacheManager = pluginCacheManager;
        _envOptions = envOptions.Value;
    }

    public RealTimeOptions GetOptions()
    {
        var plugin = TryGetPlugin();
        if (plugin?.Configurations is null)
            return _envOptions; // Sem plugin => usa o ambiente.

        var cfg = plugin.Configurations;

        var originsRaw = cfg.GetConfigurationValue("AllowedOrigins");

        return new RealTimeOptions
        {
            HubPath = NullIfEmpty(cfg.GetConfigurationValue("HubPath")) ?? _envOptions.HubPath,
            ApiKey = NullIfEmpty(cfg.GetConfigurationValue("ApiKey")) ?? _envOptions.ApiKey,
            // Chave ausente => fallback ambiente; presente (mesmo vazia) => valor do plugin.
            AllowedOrigins = originsRaw is null ? _envOptions.AllowedOrigins : ParseOrigins(originsRaw)
        };
    }

    private Plugin? TryGetPlugin()
    {
        try
        {
            // Garante que o cache esteja carregado (no-op se já estiver).
            _pluginCacheManager.LoadPluginsAsync().GetAwaiter().GetResult();
            return _pluginCacheManager.GetCachedPluginByName(PluginName);
        }
        catch
        {
            // Plugin inexistente / cache indisponível => fallback ambiente.
            return null;
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string[] ParseOrigins(string raw) =>
        raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
