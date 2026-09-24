using System.Security.Cryptography;
using Cime.BuildingBlocks.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using solvace.prform.application.Security;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.application.UserIntegrations;

/// <summary>Valores já descriptografados de um usuário: pluginId → { chave: valor }.</summary>
public sealed class UserPluginValues
{
    public Dictionary<int, Dictionary<string, string>> Values { get; init; } = new();
    public Dictionary<int, DateTimeOffset?> UpdatedAt { get; init; } = new();

    public Dictionary<string, string> For(int pluginId) =>
        Values.TryGetValue(pluginId, out var v) ? v : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public interface IUserPluginConfigurationApplication
{
    Task<IReadOnlyList<UserIntegrationResponse>> ListAsync(Guid userExternalId, CancellationToken cancellationToken);
    Task<UserIntegrationResponse> SaveAsync(Guid userExternalId, int pluginId, SaveUserIntegrationRequest request, CancellationToken cancellationToken);
    Task<UserIntegrationStatusResponse> GetStatusAsync(Guid userExternalId, CancellationToken cancellationToken);

    /// <summary>Valores do usuário (cache; bypassCache relê do banco).</summary>
    Task<UserPluginValues> GetUserValuesAsync(Guid userExternalId, CancellationToken cancellationToken, bool bypassCache = false);

    /// <summary>Plugins de uso pessoal ativos (não excluídos).</summary>
    Task<IReadOnlyList<Plugin>> GetPersonalPluginsAsync(CancellationToken cancellationToken);

    /// <summary>Configurado = todos os campos DO USUÁRIO preenchidos (os fixos vêm do global).</summary>
    bool IsConfigured(Plugin plugin, IReadOnlyDictionary<string, string> userValues);

    /// <summary>
    /// Configuração efetiva de um plugin pessoal: campos fixos com o valor global + campos do
    /// usuário com o valor dele (sem fallback para o global).
    /// </summary>
    Dictionary<string, string> BuildEffectiveValues(Plugin plugin, IReadOnlyDictionary<string, string> userValues);
}

/// <summary>
/// "Minhas integrações": valores de cada usuário para os plugins de uso pessoal. Os campos vêm
/// do plugin global (modelo); segredos são gravados criptografados (contexto = plugin + usuário)
/// e nunca voltam na resposta. Cache por usuário com a versão global na chave (recarga de
/// plugin invalida todos) e remoção ao salvar.
/// </summary>
public class UserPluginConfigurationApplication : IUserPluginConfigurationApplication
{
    // Menor que os 24 h do cache global: com várias instâncias (Cloud Run), o que o usuário salva
    // numa instância só chega às outras quando o cache delas expira.
    private const int UserCacheMinutes = 30;

    private readonly DefaultContext _context;
    private readonly IPluginCacheManager _pluginCacheManager;
    private readonly ISecretProtector _secretProtector;
    private readonly ICacheService _cacheService;
    private readonly ILogger<UserPluginConfigurationApplication> _logger;

    public UserPluginConfigurationApplication(DefaultContext context, IPluginCacheManager pluginCacheManager,
        ISecretProtector secretProtector, ICacheService cacheService, ILogger<UserPluginConfigurationApplication> logger)
    {
        _context = context;
        _pluginCacheManager = pluginCacheManager;
        _secretProtector = secretProtector;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Plugin>> GetPersonalPluginsAsync(CancellationToken cancellationToken)
    {
        var plugins = _pluginCacheManager.GetCachedPlugins();
        if (plugins is null)
        {
            await _pluginCacheManager.RefreshPluginsAsync(cancellationToken);
            plugins = _pluginCacheManager.GetCachedPlugins() ?? Enumerable.Empty<Plugin>();
        }

        // O cache só tem plugins não excluídos; desmarcado como pessoal = não aparece (spec 8).
        return plugins.Where(p => p.IsPersonal && !p.IsDeleted).OrderBy(p => p.Description).ToList();
    }

    public async Task<IReadOnlyList<UserIntegrationResponse>> ListAsync(Guid userExternalId, CancellationToken cancellationToken)
    {
        var plugins = await GetPersonalPluginsAsync(cancellationToken);
        var values = await GetUserValuesAsync(userExternalId, cancellationToken);
        return plugins.Select(p => ToResponse(p, values)).ToList();
    }

    public async Task<UserIntegrationStatusResponse> GetStatusAsync(Guid userExternalId, CancellationToken cancellationToken)
    {
        var plugins = await GetPersonalPluginsAsync(cancellationToken);
        var values = await GetUserValuesAsync(userExternalId, cancellationToken);

        var pending = plugins
            .Where(p => !IsConfigured(p, values.For(p.Id)))
            .Select(p => new UserIntegrationPendingResponse { PluginId = p.Id, Description = p.Description })
            .ToList();

        return new UserIntegrationStatusResponse { Ready = pending.Count == 0, Pending = pending };
    }

    public async Task<UserIntegrationResponse> SaveAsync(Guid userExternalId, int pluginId, SaveUserIntegrationRequest request, CancellationToken cancellationToken)
    {
        if (userExternalId == Guid.Empty)
            throw new DomainException("Usuário não identificado");

        var plugin = (await GetPersonalPluginsAsync(cancellationToken)).FirstOrDefault(p => p.Id == pluginId)
                     ?? throw new DomainException($"Plugin {pluginId} não é de uso pessoal ou não existe");

        var templateKeys = TemplateKeys(plugin);
        var incoming = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in request.Values ?? new())
            incoming[k.Trim()] = v; // chaves repetidas com caixa diferente: vale a última

        var unknown = incoming.Keys.Where(k => !templateKeys.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
            throw new DomainException($"Campos que não existem no plugin {plugin.Description}: {string.Join(", ", unknown)}");

        var fixedKeys = incoming.Keys.Where(k => !plugin.IsUserField(k)).ToList();
        if (fixedKeys.Count > 0)
            throw new DomainException($"Campos definidos pelo administrador não podem ser alterados: {string.Join(", ", fixedKeys)}");

        var entity = await _context.UserPluginConfigurations
            .FirstOrDefaultAsync(x => x.PluginId == pluginId && x.UserExternalId == userExternalId, cancellationToken);
        var stored = Deserialize(entity?.Options);
        var context = ProtectionContext(pluginId, userExternalId);

        // Recria só com as chaves do usuário no modelo atual (removidas do global ou que viraram
        // fixas somem daqui).
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in templateKeys.Where(plugin.IsUserField))
        {
            stored.TryGetValue(key, out var current);
            if (!incoming.TryGetValue(key, out var value) || value is null)
            {
                // Omitido/null = mantém o que estava salvo.
                if (!string.IsNullOrEmpty(current))
                    result[key] = current;
                continue;
            }

            if (value.Length == 0)
                continue; // "" = limpa

            result[key] = SensitiveFieldPolicy.IsSensitive(key)
                ? _secretProtector.Protect(value, context) // sem chave configurada lança (nunca grava em texto puro)
                : value;
        }

        var json = JsonConvert.SerializeObject(result);
        if (entity is null)
        {
            entity = new UserPluginConfiguration(pluginId, userExternalId, json);
            await _context.UserPluginConfigurations.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity.SetOptions(json);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _cacheService.Remove(CacheKey(userExternalId));

        var values = await GetUserValuesAsync(userExternalId, cancellationToken, bypassCache: true);
        return ToResponse(plugin, values);
    }

    public async Task<UserPluginValues> GetUserValuesAsync(Guid userExternalId, CancellationToken cancellationToken, bool bypassCache = false)
    {
        var key = CacheKey(userExternalId);
        if (!bypassCache && _cacheService.TryGetValue<UserPluginValues>(key, out var cached) && cached is not null)
            return cached;

        var rows = await _context.UserPluginConfigurations
            .AsNoTracking()
            .Where(x => x.UserExternalId == userExternalId)
            .ToListAsync(cancellationToken);

        var values = new UserPluginValues();
        foreach (var row in rows)
        {
            var context = ProtectionContext(row.PluginId, userExternalId);
            var plain = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in Deserialize(row.Options))
            {
                if (!_secretProtector.IsProtected(v))
                {
                    plain[k] = v;
                    continue;
                }

                try
                {
                    plain[k] = _secretProtector.Unprotect(v, context);
                }
                catch (Exception e) when (e is CryptographicException or InvalidOperationException or FormatException)
                {
                    // Chave trocada/ausente ou valor corrompido: trata como não preenchido (o usuário salva de novo).
                    _logger.LogWarning("Não foi possível ler o campo {Key} do plugin {PluginId} do usuário {User}: {Error}",
                        k, row.PluginId, userExternalId, e.Message);
                }
            }

            values.Values[row.PluginId] = plain;
            values.UpdatedAt[row.PluginId] = row.UpdatedAt ?? row.CreatedAt;
        }

        _cacheService.Set(key, values, UserCacheMinutes);
        return values;
    }

    public bool IsConfigured(Plugin plugin, IReadOnlyDictionary<string, string> userValues) =>
        TemplateKeys(plugin).Where(plugin.IsUserField)
            .All(k => userValues.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v));

    public Dictionary<string, string> BuildEffectiveValues(Plugin plugin, IReadOnlyDictionary<string, string> userValues)
    {
        var template = plugin.Configurations?.GetAllConfigurations() ?? new Dictionary<string, string>();
        var effective = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, globalValue) in template)
        {
            if (!plugin.IsUserField(key))
                effective[key] = globalValue; // fixo: definido pelo administrador
            else if (userValues.TryGetValue(key, out var mine))
                effective[key] = mine;
        }
        return effective;
    }

    private UserIntegrationResponse ToResponse(Plugin plugin, UserPluginValues values)
    {
        var mine = values.For(plugin.Id);
        var template = plugin.Configurations?.GetAllConfigurations() ?? new Dictionary<string, string>();

        var fields = template.Select(t =>
        {
            var sensitive = SensitiveFieldPolicy.IsSensitive(t.Key);

            if (!plugin.IsUserField(t.Key))
            {
                // Fixo: valor da configuração global, somente leitura (segredo nunca é exposto).
                return new UserIntegrationFieldResponse
                {
                    Key = t.Key,
                    Editable = false,
                    Sensitive = sensitive,
                    HasValue = !string.IsNullOrEmpty(t.Value),
                    Value = sensitive || string.IsNullOrEmpty(t.Value) ? null : t.Value
                };
            }

            var hasValue = mine.TryGetValue(t.Key, out var userValue) && !string.IsNullOrEmpty(userValue);

            if (sensitive)
                return new UserIntegrationFieldResponse { Key = t.Key, Sensitive = true, HasValue = hasValue };

            // Não sensível sem valor salvo: sugere o valor global (D7) para não redigitar.
            return hasValue
                ? new UserIntegrationFieldResponse { Key = t.Key, Value = userValue, HasValue = true }
                : new UserIntegrationFieldResponse
                {
                    Key = t.Key,
                    Value = string.IsNullOrEmpty(t.Value) ? null : t.Value,
                    Suggested = !string.IsNullOrEmpty(t.Value)
                };
        }).ToList();

        return new UserIntegrationResponse
        {
            PluginId = plugin.Id,
            Description = plugin.Description,
            Configured = IsConfigured(plugin, mine),
            Fields = fields,
            UpdatedAt = values.UpdatedAt.TryGetValue(plugin.Id, out var updated) ? updated : null
        };
    }

    private static List<string> TemplateKeys(Plugin plugin) =>
        plugin.Configurations?.GetAllConfigurations()?.Keys.ToList() ?? new List<string>();

    private static Dictionary<string, string> Deserialize(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(options);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private string CacheKey(Guid userExternalId) =>
        $"plugins:user:{_pluginCacheManager.GetConfigurationVersion()}:{userExternalId:D}";

    private static string ProtectionContext(int pluginId, Guid userExternalId) => $"{pluginId}:{userExternalId:D}";
}
