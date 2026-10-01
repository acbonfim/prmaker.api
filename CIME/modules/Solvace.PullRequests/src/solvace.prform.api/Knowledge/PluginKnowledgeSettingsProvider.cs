using System.Text.Json;
using System.Text.RegularExpressions;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Extensions;

namespace solvace.prform.Knowledge;

/// <summary>Lê o plugin "Knowledge Center Configurations"; sem plugin/valor inválido vale o padrão (dev, só o piso).</summary>
public partial class PluginKnowledgeSettingsProvider(IPluginConfigurationResolver resolver, ILogger<PluginKnowledgeSettingsProvider> logger)
    : IKnowledgeSettingsProvider
{
    public async Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, string> values;
        try
        {
            var config = await resolver.GetEffectiveConfigurationAsync(KnowledgeCenterConfigurationKeys.PluginName, cancellationToken);
            values = new Dictionary<string, string>(
                (string.IsNullOrWhiteSpace(config.Options) ? null : config.Options.JsonToListOfDictionaries().FirstOrDefault()) ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            logger.LogWarning("Plugin {Plugin} indisponível: {Error} — usando dev e só o piso do filtro.", KnowledgeCenterConfigurationKeys.PluginName, e.Message);
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var environment = KnowledgeEnvironment.Dev;
        try { environment = KnowledgeEnvironment.Normalize(values.GetValueOrDefault(KnowledgeCenterConfigurationKeys.Environment, KnowledgeEnvironment.Dev)); }
        catch (DomainException) { logger.LogWarning("Environment inválido no plugin do KC — usando dev."); }

        return new KnowledgeSettings(environment, new KnowledgeFilterOptions
        {
            ExtraPatterns = Strings(values.GetValueOrDefault(KnowledgeCenterConfigurationKeys.ExcludePatterns)),
            ExcludedArticles = ArticleNumbers(values.GetValueOrDefault(KnowledgeCenterConfigurationKeys.ExcludeArticles)),
            AllowedArticles = ArticleNumbers(values.GetValueOrDefault(KnowledgeCenterConfigurationKeys.AllowArticles)),
            MinTextLength = int.TryParse(values.GetValueOrDefault(KnowledgeCenterConfigurationKeys.MinTextLength), out var min) ? min : 0
        });
    }

    private static List<string> Strings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<JsonElement>>(json)?
                .Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Where(s => s.Length > 0).ToList() ?? [];
        }
        catch (JsonException) { return []; }
    }

    /// <summary>Aceita 12, "12", "ART-12", "art 12".</summary>
    private static HashSet<int> ArticleNumbers(string? json)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            foreach (var e in JsonSerializer.Deserialize<List<JsonElement>>(json) ?? [])
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) result.Add(n);
                else if (e.ValueKind == JsonValueKind.String && Digits().Match(e.GetString() ?? "") is { Success: true } m) result.Add(int.Parse(m.Value));
            }
        }
        catch (JsonException) { }
        return result;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
