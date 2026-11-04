using System.Security.Claims;
using solvace.azure.application.Contract;
using solvace.azure.domain.Exceptions;
using solvace.executionplans.application.Contracts;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Extensions;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// 0075: o card no DevOps para o plano de execução — branches de QA e a automação de Dev Test in QA (Skills
/// Configurations), mover o card (mesmo caminho do endpoint/MCP, com a integração de quem está chamando), estado/coluna
/// para o resumo da situação e o comentário da situação na discussion.
/// </summary>
public class ExecutionCardDevOps(
    IPluginConfigurationResolver resolver,
    DevOpsActionRunner runner,
    IAzureService azure,
    IHttpContextAccessor http,
    ILogger<ExecutionCardDevOps> logger) : IExecutionCardDevOps
{
    public async Task<ExecutionQaSettings> GetQaSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = await resolver.GetEffectiveConfigurationAsync(SkillsConfigurationKeys.PluginName, cancellationToken);
            var values = new Dictionary<string, string>(
                (string.IsNullOrWhiteSpace(config.Options) ? null : config.Options.JsonToListOfDictionaries().FirstOrDefault()) ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
            var auto = !values.TryGetValue(SkillsConfigurationKeys.AutoDevTestInQa, out var a) || !IsOff(a);
            var branches = values.TryGetValue(SkillsConfigurationKeys.QaBranchPattern, out var b)
                ? b.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : [];
            var post = !values.TryGetValue(SkillsConfigurationKeys.SituationPostToDevOps, out var p) || !IsOff(p);
            return new ExecutionQaSettings(auto, branches.Count > 0 ? branches : ExecutionQaSettings.Default.QaBranches, post);
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            logger.LogWarning("Plugin {Plugin} indisponível: {Error} — Dev Test in QA com a configuração padrão.", SkillsConfigurationKeys.PluginName, e.Message);
            return ExecutionQaSettings.Default;
        }
    }

    public async Task<string?> MoveToDevTestInQaAsync(string cardNumber, CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(cardNumber, a => a.MoveToDevTestInQaAsync(cardNumber, cancellationToken), User, cancellationToken);
            return null;
        }
        catch (DevOpsActionException e)
        {
            return e.Message;
        }
        catch (PersonalIntegrationRequiredException e)
        {
            return e.Message;
        }
    }

    public async Task<(string? State, string? Column)> GetStateAsync(string cardNumber, CancellationToken cancellationToken)
    {
        if (!int.TryParse(cardNumber.Trim(), out var id))
            return (null, null);
        var summary = (await azure.GetCardsSummaryAsync([id], cancellationToken)).FirstOrDefault();
        return (summary?.State, summary?.BoardColumn);
    }

    public Task<int> UpsertCommentAsync(string cardNumber, string html, int? commentId, CancellationToken cancellationToken) =>
        azure.UpsertCommentAsync(cardNumber.Trim(), html, commentId, cancellationToken);

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    private static bool IsOff(string value) =>
        value.Trim().ToLowerInvariant() is "false" or "0" or "off" or "nao" or "não" or "no";
}
