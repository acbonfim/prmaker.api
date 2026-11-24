using System.Globalization;
using System.Text.Json;
using solvace.executionplans.domain.Responses;
using solvace.prform.Skills;

namespace solvace.prform.AwsDeploy;

/// <summary>
/// 0100: configuração da aba AWS no "Skills Configurations" — ambientes (<c>AwsDeployEnvironments</c>) e o acompanhamento
/// do deploy em andamento (<c>AwsDeployFollowMinutes</c>/<c>AwsDeployFollowMax</c>). Valor ausente ou inválido → padrão.
/// </summary>
public static class AwsDeploySettings
{
    /// <summary>O mesmo da migração <c>SeedAwsDeploy0100.Environments</c>.</summary>
    public const string DefaultEnvironmentsJson = """
[
  { "key": "dev", "label": "DEV", "host": "devsp.solvacelabs.com", "order": 1 },
  { "key": "qa", "label": "QA", "host": "qa.solvacelabs.com", "order": 2 },
  { "key": "sandboxefeso", "label": "Sandboxefeso", "host": "sandboxefeso.solvacelabs.com", "order": 3 },
  { "key": "sandboxrt", "label": "SandboxRT", "host": "sandboxrt.solvacelabs.com", "order": 4 },
  { "key": "prod", "label": "Produção", "host": "efeso.solvacelabs.com", "order": 5 }
]
""";

    public const int DefaultFollowMinutes = 2;
    public const int DefaultFollowMax = 15;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AwsDeployConfigResponse From(IReadOnlyDictionary<string, string> settings)
    {
        settings.TryGetValue(SkillsConfigurationKeys.AwsDeployEnvironments, out var envs);
        settings.TryGetValue(SkillsConfigurationKeys.AwsDeployFollowMinutes, out var minutes);
        settings.TryGetValue(SkillsConfigurationKeys.AwsDeployFollowMax, out var max);
        return new AwsDeployConfigResponse
        {
            Environments = ParseEnvironments(envs) ?? ParseEnvironments(DefaultEnvironmentsJson)!,
            RefreshFollowMinutes = Int(minutes, DefaultFollowMinutes, 1, 60),
            RefreshFollowMax = Int(max, DefaultFollowMax, 0, 100),
        };
    }

    /// <summary>Lista válida (chave e host em cada linha), na ordem; null = vazia ou inválida.</summary>
    public static List<AwsEnvironmentDto>? ParseEnvironments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var list = JsonSerializer.Deserialize<List<AwsEnvironmentDto>>(json, Json);
            var valid = (list ?? [])
                .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Key) && !string.IsNullOrWhiteSpace(e.Host))
                .Select((e, i) => new AwsEnvironmentDto
                {
                    Key = e.Key.Trim().ToLowerInvariant(),
                    Label = string.IsNullOrWhiteSpace(e.Label) ? e.Key.Trim() : e.Label.Trim(),
                    Host = e.Host.Trim().ToLowerInvariant(),
                    Tier = string.IsNullOrWhiteSpace(e.Tier) ? null : e.Tier.Trim().ToLowerInvariant(),
                    Order = e.Order != 0 ? e.Order : i + 1,
                })
                .GroupBy(e => e.Key).Select(g => g.First())
                .OrderBy(e => e.Order).ToList();
            return valid.Count == 0 ? null : valid;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int Int(string? value, int fallback, int min, int max) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, min, max) : fallback;
}
