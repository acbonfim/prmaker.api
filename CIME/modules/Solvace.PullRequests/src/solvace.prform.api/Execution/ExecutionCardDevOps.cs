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
        catch (Exception e) when (e is DevOpsActionException or PersonalIntegrationRequiredException or InvalidOperationException or HttpRequestException)
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

    public async Task<CardEffort?> GetEffortAsync(string cardNumber, CancellationToken cancellationToken)
    {
        try
        {
            var e = await runner.EffortAsync(cardNumber.Trim(), cancellationToken);
            return new CardEffort(e.OriginalEstimate, e.CompletedWork, e.RemainingWork);
        }
        catch (Exception ex) when (ex is DevOpsActionException or PersonalIntegrationRequiredException or InvalidOperationException or HttpRequestException)
        {
            return null;
        }
    }

    public Task<(bool Ok, string Message)> SetInitialEstimateAsync(string cardNumber, CancellationToken cancellationToken) =>
        RunAsync(cardNumber, a => a.SetInitialEstimateAsync(cardNumber.Trim(), cancellationToken), cancellationToken);

    public Task<(bool Ok, string Message)> LogHoursAsync(string cardNumber, decimal hours, CancellationToken cancellationToken) =>
        RunAsync(cardNumber, a => a.LogHoursAsync(cardNumber.Trim(), hours, cancellationToken), cancellationToken);

    public async Task<ExecutionAssistantSettings> GetAssistantSettingsAsync(CancellationToken cancellationToken)
    {
        var d = ExecutionAssistantSettings.Default;
        try
        {
            var values = await SkillsValuesAsync(cancellationToken);
            var enabled = !values.TryGetValue(SkillsConfigurationKeys.AssistantEnabled, out var en) || !IsOff(en);
            var steps = values.TryGetValue(SkillsConfigurationKeys.AssistantHoursSteps, out var st)
                ? st.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : d.HoursSteps.ToList();
            var perStep = Number(values.GetValueOrDefault(SkillsConfigurationKeys.AssistantHoursPerStep)) ?? d.HoursPerStep;
            var average = Number(values.GetValueOrDefault(SkillsConfigurationKeys.AssistantAverageHours)) ?? d.AverageHours;
            return new ExecutionAssistantSettings(enabled, steps, Math.Clamp(perStep, 0.5m, 8m), Math.Clamp(average, 0.5m, 80m));
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            return d;
        }
    }

    private async Task<(bool Ok, string Message)> RunAsync(string cardNumber, Func<IDevOpsActionsService, Task<solvace.azure.domain.Models.DevOpsActionResponse>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await runner.RunAsync(cardNumber.Trim(), action, User, cancellationToken);
            return (true, result.Message);
        }
        catch (Exception e) when (e is DevOpsActionException or PersonalIntegrationRequiredException or InvalidOperationException or HttpRequestException)
        {
            // Sem integração do DevOps ("Plugin ... não encontrado"), regra não atendida, DevOps fora: a sugestão fica
            // como falhou, com a mensagem — nunca 500.
            return (false, e.Message);
        }
    }

    private async Task<Dictionary<string, string>> SkillsValuesAsync(CancellationToken cancellationToken)
    {
        var config = await resolver.GetEffectiveConfigurationAsync(SkillsConfigurationKeys.PluginName, cancellationToken);
        return new Dictionary<string, string>(
            (string.IsNullOrWhiteSpace(config.Options) ? null : config.Options.JsonToListOfDictionaries().FirstOrDefault()) ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static decimal? Number(string? value) =>
        decimal.TryParse(value?.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    private static bool IsOff(string value) =>
        value.Trim().ToLowerInvariant() is "false" or "0" or "off" or "nao" or "não" or "no";
}
