using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Extensions;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// 0076: as etapas da análise em <see cref="SkillsConfigurationKeys.EvidenceGateSteps"/> (padrão <c>montar-analise</c>) só
/// concluem com as evidências do card importadas e todas as legíveis abertas pela skill (ou com o motivo do usuário para
/// seguir sem elas).
/// </summary>
public class EvidenceGate(IExecutionPlanApplication plans, IPluginConfigurationResolver resolver, ILogger<EvidenceGate> logger)
{
    public static readonly string[] DefaultSteps = ["montar-analise"];

    public async Task<string?> CheckAsync(Guid planId, string stepKey, CancellationToken ct)
    {
        var key = ExecutionStep.NormalizeKey(stepKey);
        return (await GateStepsAsync(ct)).Contains(key) ? await plans.CheckEvidenceAsync(planId, ct) : null;
    }

    private async Task<HashSet<string>> GateStepsAsync(CancellationToken ct)
    {
        try
        {
            var config = await resolver.GetEffectiveConfigurationAsync(SkillsConfigurationKeys.PluginName, ct);
            var values = (string.IsNullOrWhiteSpace(config.Options) ? null : config.Options.JsonToListOfDictionaries().FirstOrDefault())
                         ?? new Dictionary<string, string>();
            var raw = values.FirstOrDefault(kv => kv.Key.Equals(SkillsConfigurationKeys.EvidenceGateSteps, StringComparison.OrdinalIgnoreCase));
            if (raw.Key is not null)
                return raw.Value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.ToLowerInvariant()).ToHashSet();
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            logger.LogWarning("Plugin {Plugin} indisponível: {Error} — evidências com as etapas padrão.", SkillsConfigurationKeys.PluginName, e.Message);
        }
        return DefaultSteps.ToHashSet();
    }
}
