using Microsoft.AspNetCore.Http;
using solvace.prform.domain.Entities;

namespace solvace.prform.application.UserIntegrations;

/// <summary>
/// Porta única para as integrações lerem a configuração de um plugin:
/// plugin comum → configuração global (como sempre); plugin de uso pessoal → valores do usuário
/// da requisição (claim ExternalId), sem fallback para o global (D1). Sem configuração pessoal
/// completa → <see cref="PersonalIntegrationRequiredException"/> (403).
/// </summary>
public interface IPluginConfigurationResolver
{
    Task<PluginConfiguration> GetEffectiveConfigurationAsync(string pluginName, CancellationToken cancellationToken = default);

    /// <summary>ExternalId do usuário da requisição (null fora de uma requisição autenticada).</summary>
    Guid? CurrentUserExternalId { get; }
}

public class PluginConfigurationResolver : IPluginConfigurationResolver
{
    private readonly IPluginCacheManager _pluginCacheManager;
    private readonly IUserPluginConfigurationApplication _userConfigurations;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PluginConfigurationResolver(IPluginCacheManager pluginCacheManager,
        IUserPluginConfigurationApplication userConfigurations, IHttpContextAccessor httpContextAccessor)
    {
        _pluginCacheManager = pluginCacheManager;
        _userConfigurations = userConfigurations;
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? CurrentUserExternalId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User.FindFirst("ExternalId")?.Value;
            return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
        }
    }

    public async Task<PluginConfiguration> GetEffectiveConfigurationAsync(string pluginName, CancellationToken cancellationToken = default)
    {
        var plugin = await FindPluginAsync(pluginName, cancellationToken)
                     ?? throw new InvalidOperationException($"Plugin '{pluginName}' não encontrado");

        if (!plugin.IsPersonal)
            return plugin.Configurations;

        var user = CurrentUserExternalId ?? throw new PersonalIntegrationRequiredException(new[] { plugin.Description });

        var values = await _userConfigurations.GetUserValuesAsync(user, cancellationToken);
        var mine = values.For(plugin.Id);
        if (!_userConfigurations.IsConfigured(plugin, mine))
        {
            // Pode ter acabado de salvar em outra instância: confere no banco antes de bloquear.
            values = await _userConfigurations.GetUserValuesAsync(user, cancellationToken, bypassCache: true);
            mine = values.For(plugin.Id);
            if (!_userConfigurations.IsConfigured(plugin, mine))
                throw new PersonalIntegrationRequiredException(new[] { plugin.Description });
        }

        return new PluginConfiguration(new List<IDictionary<string, string>> { new Dictionary<string, string>(mine) });
    }

    private async Task<Plugin?> FindPluginAsync(string name, CancellationToken cancellationToken)
    {
        var plugin = Find(name);
        if (plugin is not null)
            return plugin;

        await _pluginCacheManager.RefreshPluginsAsync(cancellationToken);
        return Find(name);
    }

    private Plugin? Find(string name) =>
        _pluginCacheManager.GetCachedPlugins()?
            .FirstOrDefault(p => string.Equals(p.Description, name, StringComparison.OrdinalIgnoreCase));
}
