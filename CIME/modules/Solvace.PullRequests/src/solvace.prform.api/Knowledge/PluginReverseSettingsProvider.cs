using System.Globalization;
using System.Text.Json;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Reverse;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Extensions;
using solvace.prform.Skills;

namespace solvace.prform.Knowledge;

/// <summary>
/// Configuração da engenharia reversa (0052) no plugin "Skills Configurations": quem aprova/publica, documentos exigidos
/// para o módulo contar como completo, etapa travada da analisar-bug, cobertura mínima e modelos que substituem os do
/// código. Sem plugin/valor inválido vale o padrão.
/// </summary>
public class PluginReverseSettingsProvider(IPluginConfigurationResolver resolver, ILogger<PluginReverseSettingsProvider> logger) : IReverseSettingsProvider
{
    public async Task<ReverseSettings> GetAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, string> values;
        try
        {
            var config = await resolver.GetEffectiveConfigurationAsync(SkillsConfigurationKeys.PluginName, cancellationToken);
            values = new Dictionary<string, string>(
                (string.IsNullOrWhiteSpace(config.Options) ? null : config.Options.JsonToListOfDictionaries().FirstOrDefault()) ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            logger.LogWarning("Plugin {Plugin} indisponível: {Error} — engenharia reversa com a configuração padrão.", SkillsConfigurationKeys.PluginName, e.Message);
            return ReverseSettings.Default;
        }

        var d = ReverseSettings.Default;
        var roles = List(values.GetValueOrDefault(SkillsConfigurationKeys.ReverseEngineeringApproverRoles));
        var required = List(values.GetValueOrDefault(SkillsConfigurationKeys.ReverseEngineeringRequiredDocs))
            .Select(x => x.ToLowerInvariant()).Where(ReverseDocTypes.ByKey.ContainsKey).ToList();
        var gate = values.TryGetValue(SkillsConfigurationKeys.ReverseEngineeringGateStep, out var g) ? g.Trim() : d.GateStep;
        var coverage = double.TryParse(values.GetValueOrDefault(SkillsConfigurationKeys.ReverseEngineeringMinCoverage)?.Trim().TrimEnd('%'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? (c > 1 ? c / 100 : c) : d.MinCoverage;
        return new ReverseSettings(
            roles.Count > 0 ? roles : d.ApproverRoles,
            required.Count > 0 ? required : d.RequiredDocs,
            string.IsNullOrWhiteSpace(gate) ? null : gate,
            Math.Clamp(coverage, 0, 1),
            Templates(values.GetValueOrDefault(SkillsConfigurationKeys.ReverseEngineeringTemplates)));
    }

    /// <summary>Aceita "a,b", "a; b" ou ["a","b"].</summary>
    private static List<string> List(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var v = value.Trim();
        if (v.StartsWith('['))
            try { return JsonSerializer.Deserialize<List<string>>(v)?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? []; }
            catch (JsonException) { return []; }
        return v.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private Dictionary<string, string> Templates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [])
                .Where(kv => ReverseDocTypes.ByKey.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        catch (JsonException)
        {
            logger.LogWarning("ReverseEngineeringTemplates inválido no plugin — usando os modelos do código.");
            return [];
        }
    }
}
