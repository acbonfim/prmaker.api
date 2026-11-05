using solvace.prform.application.Security;
using solvace.prform.domain.Entities;

namespace solvace.prform.PluginSettings;

/// <summary>
/// 0086: o catálogo resolvido contra os plugins (sem banco): valor, existe, é o padrão, rótulo/ajuda do FieldSettings
/// do admin por cima do catálogo, campo pessoal, chaves fora do catálogo e plugins que faltam. Segredo nunca sai em valor.
/// </summary>
public static class PluginSettingsResolver
{
    public static SettingsResponse Build(IEnumerable<Plugin> source)
    {
        var plugins = source.Where(p => !p.IsDeleted).OrderBy(p => p.Id).ToList();
        var values = plugins.ToDictionary(p => p.Id, ValuesOf);
        var response = new SettingsResponse();

        foreach (var section in PluginSettingsCatalog.Sections)
        {
            var sectionResponse = new SettingSectionResponse
                { Key = section.Key, Title = section.Title, Icon = section.Icon, Description = section.Description };
            foreach (var group in section.Groups)
            {
                sectionResponse.Groups.Add(new SettingGroupResponse
                {
                    Key = group.Key, Title = group.Title, Description = group.Description,
                    Fields = PluginSettingsCatalog.Fields
                        .Where(f => f.Section == section.Key && f.Group == group.Key)
                        .Select(f =>
                        {
                            var plugin = PluginOf(f, plugins);
                            return Field(f, plugin, plugin is null ? null : values[plugin.Id]);
                        })
                        .ToList()
                });
            }
            response.Sections.Add(sectionResponse);
        }

        response.Plugins = plugins.Select(p => new SettingPluginResponse
        {
            Id = p.Id, Name = p.Description, Exists = true, IsPersonal = p.IsPersonal, IsOptional = p.IsOptional,
            AdminOnly = p.AdminOnly, Version = p.Configurations?.Version.ToString()
        }).ToList();

        var missing = PluginSettingsCatalog.PluginRefs.Where(r => !plugins.Any(r.Matches)).ToList();
        response.MissingPlugins = missing.Select(r => r.Name).ToList();
        response.Plugins.AddRange(missing.Select(r => new SettingPluginResponse { Id = r.Id, Name = r.Name, Exists = false }));

        foreach (var plugin in plugins)
        foreach (var (key, value) in values[plugin.Id])
        {
            if (PluginSettingsCatalog.Find(plugin, key) is not null)
                continue;
            var secret = SensitiveFieldPolicy.IsSensitive(key);
            response.Uncataloged.Add(new UncatalogedSettingResponse
                { PluginId = plugin.Id, PluginName = plugin.Description, Key = key, Value = secret ? null : value, Secret = secret });
        }

        return response;
    }

    /// <summary>Campo resolvido de uma chave (catalogada ou não) — usado na resposta do PATCH.</summary>
    public static SettingFieldResponse ResolveField(Plugin plugin, string key)
    {
        var values = ValuesOf(plugin);
        if (PluginSettingsCatalog.Find(plugin, key) is { } def)
            return Field(def, plugin, values);

        var secret = SensitiveFieldPolicy.IsSensitive(key);
        var exists = values.TryGetValue(key, out var value);
        return new SettingFieldResponse
        {
            Id = $"{plugin.Id}:{key}", PluginId = plugin.Id, PluginName = plugin.Description, Key = key,
            Label = plugin.GetFieldLabel(key) ?? key, Help = plugin.GetFieldHelp(key),
            Type = secret ? SettingTypes.Secret : SettingTypes.Text,
            Value = secret || !exists ? null : value, HasValue = exists && !string.IsNullOrEmpty(value), Exists = exists,
            IsDefault = !exists, Personal = Personal(plugin, key)
        };
    }

    public static Plugin? PluginOf(SettingDef def, IEnumerable<Plugin> plugins) =>
        plugins.Where(p => !p.IsDeleted).OrderBy(p => p.Id).FirstOrDefault(def.Plugin.Matches);

    /// <summary>Chaves gravadas do plugin (o [0] do Options); inválido = nenhuma.</summary>
    public static IReadOnlyDictionary<string, string> ValuesOf(Plugin plugin)
    {
        try
        {
            return PluginSettingsRules.ReadValues(plugin.Configurations?.Options);
        }
        catch (Exception e) when (e is Newtonsoft.Json.JsonException or InvalidOperationException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static SettingFieldResponse Field(SettingDef def, Plugin? plugin, IReadOnlyDictionary<string, string>? values)
    {
        string? value = null;
        var exists = values?.TryGetValue(def.Key, out value) == true;
        var secret = def.Type == SettingTypes.Secret;
        return new SettingFieldResponse
        {
            Id = plugin is null ? $"{def.Plugin.Name}:{def.Key}" : $"{plugin.Id}:{def.Key}",
            PluginId = plugin?.Id,
            PluginName = plugin?.Description ?? def.Plugin.Name,
            Key = def.Key,
            Label = plugin?.GetFieldLabel(def.Key) ?? def.Label,
            Help = plugin?.GetFieldHelp(def.Key) ?? def.Help,
            Type = def.Type,
            Value = secret || !exists ? null : value,
            HasValue = exists && !string.IsNullOrEmpty(value),
            Exists = exists,
            Default = def.Default,
            IsDefault = PluginSettingsRules.IsDefault(def, exists, value),
            Options = def.Options,
            AllowCustom = def.AllowCustom,
            Min = def.Min,
            Max = def.Max,
            Unit = def.Unit,
            ListFormat = def.ListFormat,
            StepSources = def.StepSources,
            Variables = def.Variables,
            Columns = def.Columns,
            RowKey = def.RowKey,
            MapKeyLabel = def.MapKeyLabel,
            MapValueLabel = def.MapValueLabel,
            MapValueType = def.MapValueType,
            ReadBy = def.ReadBy,
            Personal = plugin is null ? null : Personal(plugin, def.Key),
            Obsolete = def.Obsolete,
            Advanced = def.Advanced,
            MultilineHint = def.MultilineHint
        };
    }

    private static SettingPersonalResponse? Personal(Plugin plugin, string key)
    {
        if (!plugin.IsPersonal)
            return null;
        var userFills = plugin.IsUserField(key);
        return new SettingPersonalResponse
        {
            UserFills = userFills,
            Required = userFills && !plugin.IsOptionalField(key),
            UseGlobalDefault = plugin.UsesGlobalDefault(key)
        };
    }
}
