using Microsoft.EntityFrameworkCore;
using solvace.prform.application;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.PluginSettings;

/// <summary>Resultado do PATCH: 200 com a resposta, 409 com os conflitos ou 400 com os erros.</summary>
public sealed record SettingsPatchResult(SettingsPatchResponse? Ok, IReadOnlyList<SettingConflict>? Conflicts, IReadOnlyList<SettingError>? Errors);

/// <summary>
/// 0086: configurações por seção — o catálogo resolvido (GET), a gravação só das chaves enviadas com checagem do valor
/// esperado e do xmin (PATCH) e o histórico.
/// </summary>
public class PluginSettingsService(
    DefaultContext db,
    AuthenticationContext auth,
    IPluginCacheManager cache,
    IPluginApplication pluginApplication,
    IPluginSettingHistory history,
    ILogger<PluginSettingsService> logger)
{
    public const int MaxAttempts = 3;
    public const int MaxHistory = 500;

    public async Task<SettingsResponse> GetAsync(CancellationToken ct) =>
        PluginSettingsResolver.Build(await CachedPluginsAsync(ct));

    public async Task<SettingsPatchResult> PatchAsync(SettingsPatchRequest request, CancellationToken ct)
    {
        var changes = request.Changes ?? [];
        if (changes.Count == 0)
            return new(null, null, [new SettingError(string.Empty, "Nenhuma alteração enviada.")]);

        var errors = Validate(changes, (await CachedPluginsAsync(ct)).ToList());
        if (errors.Count > 0)
            return new(null, null, errors);

        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var ids = changes.Select(c => c.PluginId).Distinct().ToList();
            var plugins = await db.Plugins.Include(p => p.Configurations)
                .Where(p => ids.Contains(p.Id) && !p.IsDeleted)
                .ToListAsync(ct);

            // 1) lê o atual de cada plugin e confere o valor esperado de todas as chaves (nada é gravado se algo mudou)
            var conflicts = new List<SettingConflict>();
            var currents = new Dictionary<int, Dictionary<string, string>>();
            foreach (var group in changes.GroupBy(c => c.PluginId))
            {
                var plugin = plugins.FirstOrDefault(p => p.Id == group.Key);
                if (plugin is null)
                    return new(null, null, group.Select(c => new SettingError(IdOf(c), "Plugin não encontrado.")).ToList());

                Dictionary<string, string> current;
                try
                {
                    current = PluginSettingsRules.ReadValues(plugin.Configurations?.Options);
                }
                catch (Exception e) when (e is Newtonsoft.Json.JsonException or InvalidOperationException)
                {
                    return new(null, null, group.Select(c => new SettingError(IdOf(c),
                        $"A configuração gravada do plugin {plugin.Description} não é um JSON válido — corrija na área avançada.")).ToList());
                }
                currents[plugin.Id] = current;

                foreach (var change in group)
                {
                    if (PluginSettingsCatalog.IsSecret(plugin, change.Key))
                        continue; // segredo: sem checagem (a tela nunca viu o valor)
                    var currentValue = current.TryGetValue(change.Key, out var v) ? v : null;
                    if (!PluginSettingsRules.MatchesExpected(currentValue, change.Expected))
                        conflicts.Add(new SettingConflict(IdOf(change), change.Key, plugin.Id, currentValue, change.Expected));
                }
            }

            if (conflicts.Count > 0)
            {
                // A tela estava velha: recarrega o cache para o próximo GET vir com o valor atual.
                await cache.RefreshPluginsAsync(ct);
                return new(null, conflicts, null);
            }

            // 2) mescla só as chaves enviadas, no formato de sempre, com o histórico no mesmo SaveChanges
            var touched = new List<Plugin>();
            foreach (var group in changes.GroupBy(c => c.PluginId))
            {
                var plugin = plugins.First(p => p.Id == group.Key);
                var current = currents[plugin.Id];
                var merged = PluginSettingsRules.Merge(current, group.Select(c => (c.Key, c.Value, c.Remove)));
                if (PluginSettingDiff.Compute(current, merged).Count == 0)
                    continue; // nada mudou de fato neste plugin

                if (plugin.Configurations is null)
                    plugin.Configurations = new PluginConfiguration([merged]) { PluginId = plugin.Id };
                else
                    plugin.Configurations.Options = PluginSettingsRules.WriteValues(plugin.Configurations.Options, merged);
                plugin.UpdatedAt = DateTimeOffset.UtcNow; // UpdatedBy pelo AuditSaveChangesInterceptor
                history.Track(plugin, current, merged);
                touched.Add(plugin);
            }

            if (touched.Count == 0)
                return new(Response(plugins, changes), null, null);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outro admin salvou o mesmo plugin entre a leitura e a gravação (xmin mudou): relê e confere de novo.
                logger.LogInformation("Configurações: concorrência no xmin (tentativa {Attempt}) — relendo.", attempt);
                if (attempt < MaxAttempts)
                {
                    // espera curta e aleatória: gravações simultâneas não colidem de novo na mesma hora
                    await Task.Delay(Random.Shared.Next(40, 160) * attempt, ct);
                    continue;
                }
                db.ChangeTracker.Clear();
                await cache.RefreshPluginsAsync(ct);
                return new(null, await CurrentConflictsAsync(changes, ct), null);
            }

            await cache.RefreshPluginsAsync(ct);
            foreach (var plugin in touched)
                await pluginApplication.NotifyPullRequestConfigUpdatedAsync(plugin.Id, ct);

            var refreshed = (await CachedPluginsAsync(ct)).ToList();
            return new(Response(refreshed, changes, touched), null, null);
        }
    }

    public async Task<List<SettingHistoryResponse>> HistoryAsync(int? pluginId, string? key, int take, CancellationToken ct)
    {
        var query = db.PluginSettingChanges.AsNoTracking();
        if (pluginId is { } id)
            query = query.Where(x => x.PluginId == id);
        if (!string.IsNullOrWhiteSpace(key))
        {
            var k = key.Trim();
            query = query.Where(x => x.Key == k);
        }
        var rows = await query.OrderByDescending(x => x.ChangedAt).ThenByDescending(x => x.Id)
            .Take(Math.Clamp(take, 1, MaxHistory))
            .ToListAsync(ct);

        var plugins = (await CachedPluginsAsync(ct)).ToDictionary(p => p.Id);
        var names = await UserNamesAsync(rows.Select(r => r.ChangedBy), ct);
        return rows.Select(r =>
        {
            var plugin = plugins.GetValueOrDefault(r.PluginId);
            var secret = plugin is not null && PluginSettingsCatalog.IsSecret(plugin, r.Key);
            return new SettingHistoryResponse
            {
                PluginId = r.PluginId,
                PluginName = plugin?.Description,
                Key = r.Key,
                // Já gravado mascarado; mascara de novo por garantia.
                OldValue = secret ? PluginSettingHistory.Mask(r.OldValue) : r.OldValue,
                NewValue = secret ? PluginSettingHistory.Mask(r.NewValue) : r.NewValue,
                ChangedBy = r.ChangedBy,
                ChangedByName = r.ChangedBy is not null && names.TryGetValue(r.ChangedBy, out var name) ? name : null,
                ChangedAt = r.ChangedAt
            };
        }).ToList();
    }

    /// <summary>Esgotadas as tentativas: cada chave enviada volta como conflito, com o valor atual do banco.</summary>
    private async Task<List<SettingConflict>> CurrentConflictsAsync(IReadOnlyList<SettingChangeRequest> changes, CancellationToken ct)
    {
        var ids = changes.Select(c => c.PluginId).Distinct().ToList();
        var plugins = await db.Plugins.AsNoTracking().Include(p => p.Configurations).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        return changes.Select(c =>
        {
            var plugin = plugins.FirstOrDefault(p => p.Id == c.PluginId);
            var values = plugin is null ? new Dictionary<string, string>() : PluginSettingsResolver.ValuesOf(plugin);
            var current = values.TryGetValue(c.Key, out var v) ? v : null;
            if (plugin is not null && PluginSettingsCatalog.IsSecret(plugin, c.Key))
                current = PluginSettingHistory.Mask(current);
            return new SettingConflict(IdOf(c), c.Key, c.PluginId, current, c.Expected);
        }).ToList();
    }

    /// <summary>Erros de formato antes de abrir o banco: chave, plugin, repetição e o tipo do catálogo.</summary>
    public static List<SettingError> Validate(IReadOnlyList<SettingChangeRequest> changes, IReadOnlyList<Plugin> plugins)
    {
        var errors = new List<SettingError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            var id = IdOf(change);
            if (string.IsNullOrWhiteSpace(change.Key) || change.Key != change.Key.Trim() || change.Key.Length > PluginSettingsRules.MaxKeyLength)
            {
                errors.Add(new(id, "Chave inválida."));
                continue;
            }
            if (!seen.Add(id))
            {
                errors.Add(new(id, "A mesma chave foi enviada mais de uma vez."));
                continue;
            }
            var plugin = plugins.FirstOrDefault(p => p.Id == change.PluginId && !p.IsDeleted);
            if (plugin is null)
            {
                errors.Add(new(id, "Plugin não encontrado."));
                continue;
            }
            if (!change.Remove && PluginSettingsRules.Validate(PluginSettingsCatalog.Find(plugin, change.Key), change.Value) is { } message)
                errors.Add(new(id, message));
        }
        return errors;
    }

    private static string IdOf(SettingChangeRequest change) => $"{change.PluginId}:{change.Key}";

    private static SettingsPatchResponse Response(IReadOnlyList<Plugin> plugins, IEnumerable<SettingChangeRequest> changes,
        IReadOnlyList<Plugin>? touched = null)
    {
        var response = new SettingsPatchResponse();
        foreach (var id in changes.Select(c => c.PluginId).Distinct())
        {
            var plugin = plugins.FirstOrDefault(p => p.Id == id);
            var version = plugin?.Configurations?.Version
                          ?? touched?.FirstOrDefault(p => p.Id == id)?.Configurations?.Version;
            if (version is not null)
                response.Plugins.Add(new SettingPluginVersion(id, version.Value.ToString()));
        }
        foreach (var change in changes)
            if (plugins.FirstOrDefault(p => p.Id == change.PluginId) is { } plugin)
                response.Fields.Add(PluginSettingsResolver.ResolveField(plugin, change.Key));
        return response;
    }

    private async Task<IEnumerable<Plugin>> CachedPluginsAsync(CancellationToken ct)
    {
        var plugins = cache.GetCachedPlugins();
        if (plugins is null || !plugins.Any())
        {
            await cache.RefreshPluginsAsync(ct);
            plugins = cache.GetCachedPlugins();
        }
        return plugins ?? [];
    }

    /// <summary>Nome de quem alterou (auth, só leitura); auth fora do ar = sem nome.</summary>
    private async Task<Dictionary<string, string>> UserNamesAsync(IEnumerable<string?> externalIds, CancellationToken ct)
    {
        var ids = externalIds.Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return [];
        try
        {
            var users = await auth.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToListAsync(ct);
            return users.Where(u => !string.IsNullOrWhiteSpace(u.FullName))
                .ToDictionary(u => u.Id.ToString(), u => u.FullName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning("Histórico das configurações sem os nomes dos usuários: {Error}", e.Message);
            return [];
        }
    }
}
