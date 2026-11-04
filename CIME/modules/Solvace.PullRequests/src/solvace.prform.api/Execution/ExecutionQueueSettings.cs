using solvace.executionplans.application.Contracts;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Extensions;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// 0049: configurações da fila no "Skills Configurations" — a correção numa sessão nova do Claude. Sem a configuração,
/// o padrão. (0080: a espera depois de comentário saiu; a chave ExecutorNoteDelaySeconds não é mais lida.)
/// </summary>
public class ExecutionQueueSettings(IPluginConfigurationResolver settings, ILogger<ExecutionQueueSettings> logger) : IExecutionQueueSettings
{
    public async Task<ExecutionQueueOptions> GetAsync(CancellationToken cancellationToken)
    {
        var fallback = ExecutionQueueOptions.Default;
        try
        {
            var config = await settings.GetEffectiveConfigurationAsync(SkillsConfigurationKeys.PluginName, cancellationToken);
            var newSession = config.GetConfigurationValueOrDefault(SkillsConfigurationKeys.ExecutorCorrectionNewSession, string.Empty).Trim();
            return new ExecutionQueueOptions(bool.TryParse(newSession, out var b) ? b : fallback.CorrectionInNewSession);
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            logger.LogWarning(e, "Sem as configurações da fila no {Plugin} — vai o padrão", SkillsConfigurationKeys.PluginName);
            return fallback;
        }
    }
}
