using System.Globalization;
using System.Net;
using System.Text.Json;
using solvace.azure.application.Contract;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;
using solvace.azure.domain.Options;
using solvace.azure.domain.Requests;
using solvace.prform.application.Contracts;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;
using solvace.prform.domain.Responses;

namespace solvace.azure.application.Services;

/// <summary>
/// Ações do menu "Ações DevOps" da tela do card (feature 0011). As regras que a tela usa para
/// habilitar cada opção são revalidadas aqui com o card relido do DevOps: a tela pode estar
/// desatualizada (outra pessoa mexeu no card). Valores vêm do "AI Configurations" (pessoal,
/// com os padrões globais).
/// </summary>
public class DevOpsActionsService : IDevOpsActionsService
{
    private const string FieldWorkItemType = "System.WorkItemType";
    private const string FieldAreaPath = "System.AreaPath";
    private const string FieldState = "System.State";
    private const string FieldHistory = "System.History";
    private const string FieldOriginalEstimate = "Microsoft.VSTS.Scheduling.OriginalEstimate";
    private const string FieldRemainingWork = "Microsoft.VSTS.Scheduling.RemainingWork";
    private const string FieldCompletedWork = "Microsoft.VSTS.Scheduling.CompletedWork";

    private readonly IAzureService _azureService;
    private readonly IPluginConfigurationResolver _configurationResolver;
    private readonly IPullRequestApplication _pullRequestApplication;

    public DevOpsActionsService(IAzureService azureService, IPluginConfigurationResolver configurationResolver,
        IPullRequestApplication pullRequestApplication)
    {
        _azureService = azureService;
        _configurationResolver = configurationResolver;
        _pullRequestApplication = pullRequestApplication;
    }

    public async Task<DevOpsActionsConfigResponse> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        PluginConfiguration config;
        try
        {
            config = await _configurationResolver.GetEffectiveConfigurationAsync(AIConfigurationKeys.PluginName, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return new DevOpsActionsConfigResponse { Available = false }; // plugin não existe
        }

        string? Value(string key) => config.GetConfigurationValueOrDefault(key, string.Empty) is { Length: > 0 } v ? v.Trim() : null;

        var original = ParseNumber(Value(AIConfigurationKeys.BugInitialOriginalEstimate));
        var remaining = ParseNumber(Value(AIConfigurationKeys.BugInitialRemainingWork));
        var completed = ParseNumber(Value(AIConfigurationKeys.BugInitialCompletedWork));

        return new DevOpsActionsConfigResponse
        {
            Available = true,
            Bug = new DevOpsBugActionsConfig
            {
                SummaryPrompt = Value(AIConfigurationKeys.BugSummaryPrompt),
                TestInProduction = new DevOpsTestInProductionConfig
                {
                    RequiredArea = Value(AIConfigurationKeys.BugTestInProductionRequiredArea),
                    Area = Value(AIConfigurationKeys.BugTestInProductionArea),
                    State = Value(AIConfigurationKeys.BugTestInProductionState),
                    Comment = Value(AIConfigurationKeys.BugTestInProductionComment)
                },
                ReadyForQa = new DevOpsReadyForQaConfig { State = Value(AIConfigurationKeys.BugReadyForQaState) },
                InitialEstimate = new DevOpsInitialEstimateConfig
                {
                    Configured = original is not null && remaining is not null && completed is not null,
                    OriginalEstimate = original,
                    RemainingWork = remaining,
                    CompletedWork = completed
                }
            }
        };
    }

    public async Task<DevOpsActionResponse> MoveToTestInProductionAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var bug = (await GetConfigAsync(cancellationToken)).Bug.TestInProduction;
        if (string.IsNullOrEmpty(bug.Area) || string.IsNullOrEmpty(bug.State))
            throw DevOpsActionException.Conflict("Área e estado de destino não configurados no plugin AI Configurations");

        var card = await LoadBugAsync(cardNumber, cancellationToken);
        EnsureNoPendencies(card);

        var currentArea = GetString(card, FieldAreaPath);
        if (!string.IsNullOrEmpty(bug.RequiredArea) &&
            !string.Equals(currentArea?.Trim(), bug.RequiredArea, StringComparison.OrdinalIgnoreCase))
            throw DevOpsActionException.Conflict($"O card precisa estar na área {bug.RequiredArea} (está em {currentArea ?? "—"})");

        var fields = new Dictionary<string, object> { [FieldAreaPath] = bug.Area, [FieldState] = bug.State };
        if (!string.IsNullOrEmpty(bug.Comment))
            fields[FieldHistory] = WebUtility.HtmlEncode(bug.Comment); // vira comentário na Discussion, na mesma revisão

        var rev = await _azureService.PatchFieldsAsync(cardNumber, fields, cancellationToken);
        return new DevOpsActionResponse
        {
            Rev = rev,
            Message = $"Card movido para {bug.State} (área {bug.Area})" + (string.IsNullOrEmpty(bug.Comment) ? "" : $" com o comentário \"{bug.Comment}\"")
        };
    }

    public async Task<DevOpsActionResponse> MoveToReadyForQaAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var state = (await GetConfigAsync(cancellationToken)).Bug.ReadyForQa.State;
        if (string.IsNullOrEmpty(state))
            throw DevOpsActionException.Conflict("Estado de Ready for QA não configurado no plugin AI Configurations");

        var card = await LoadBugAsync(cardNumber, cancellationToken);
        EnsureNoPendencies(card);

        var rev = await _azureService.PatchFieldsAsync(cardNumber, new Dictionary<string, object> { [FieldState] = state }, cancellationToken);
        return new DevOpsActionResponse { Rev = rev, Message = $"Card movido para {state} (Ready for QA)" };
    }

    public async Task<DevOpsActionResponse> SetInitialEstimateAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var estimate = (await GetConfigAsync(cancellationToken)).Bug.InitialEstimate;
        if (!estimate.Configured)
            throw DevOpsActionException.Conflict("Preencha os valores da estimativa inicial em Minhas integrações → AI Configurations");

        var card = await LoadBugAsync(cardNumber, cancellationToken);
        if (GetNumber(card, FieldOriginalEstimate) is { } current && current != 0m)
            throw DevOpsActionException.Conflict($"O card já tem Original Estimate ({current.ToString(CultureInfo.InvariantCulture)})");

        var rev = await _azureService.PatchFieldsAsync(cardNumber, new Dictionary<string, object>
        {
            [FieldOriginalEstimate] = estimate.OriginalEstimate!.Value,
            [FieldRemainingWork] = estimate.RemainingWork!.Value,
            [FieldCompletedWork] = estimate.CompletedWork!.Value
        }, cancellationToken);

        return new DevOpsActionResponse
        {
            Rev = rev,
            Message = "Estimativa inicial registrada: " +
                      $"Original Estimate {Format(estimate.OriginalEstimate)}, Remaining Work {Format(estimate.RemainingWork)}, Completed Work {Format(estimate.CompletedWork)}"
        };
    }

    public async Task<DevOpsActionResponse> ZeroRemainingAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var card = await LoadBugAsync(cardNumber, cancellationToken);
        var remaining = GetNumber(card, FieldRemainingWork);
        if (remaining is null or 0m)
            throw DevOpsActionException.Conflict("O Remaining Work do card já está zerado");

        var rev = await _azureService.PatchFieldsAsync(cardNumber, new Dictionary<string, object> { [FieldRemainingWork] = 0 }, cancellationToken);
        return new DevOpsActionResponse { Rev = rev, Message = $"Remaining Work zerado (era {Format(remaining)})" };
    }

    public async Task<PullRequestRegisterResponse> SaveSummaryAsync(string cardNumber, SaveSummaryRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Summary))
            throw new DomainException("Resumo vazio");
        if (request.Publish && string.IsNullOrWhiteSpace(request.Html))
            throw new DomainException("Resumo em HTML vazio: informe o html para publicar na discussion");

        var register = await _pullRequestApplication.GetByCardNumber(cardNumber, cancellationToken)
                       ?? throw new DomainException($"Salve o card {cardNumber} no PRMake antes de salvar o resumo");

        if (!request.Publish)
            return await _pullRequestApplication.SaveSummary(register.CardNumber, request.Summary, null, cancellationToken);

        // Primeiro publica: se o DevOps falhar, nada é gravado no PRMake.
        var commentId = await _azureService.UpsertCommentAsync(register.CardNumber, request.Html!, register.SummaryCommentId, cancellationToken);
        return await _pullRequestApplication.SaveSummary(register.CardNumber, request.Summary, commentId, cancellationToken);
    }

    /// <summary>Relê o card no DevOps; só cards do tipo Bug (User Story terá outras ações).</summary>
    private async Task<AzureCardFullResponse> LoadBugAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var card = await _azureService.GetCardFullAsync(cardNumber, cancellationToken);
        if (card is null || !string.IsNullOrEmpty(card.Error) || card.Id == 0)
            throw DevOpsActionException.NotFound($"Card {cardNumber} não encontrado no DevOps");

        // Mesma regra do front: User Story é US, o resto é tratado como Bug.
        if (string.Equals(GetString(card, FieldWorkItemType), "User Story", StringComparison.OrdinalIgnoreCase))
            throw DevOpsActionException.Conflict("Ações de DevOps para User Story ainda não estão disponíveis");

        return card;
    }

    private static void EnsureNoPendencies(AzureCardFullResponse card)
    {
        var a = card.Alerts;
        var pending = new List<string>();
        if (a.MissingRootCause) pending.Add("Root Cause");
        if (a.MissingResolutionType) pending.Add("Resolution Type");
        if (a.MissingGeneralClassification) pending.Add("General Classification");
        if (a.MissingClassification) pending.Add("Classification");
        if (a.RemainingNotZero) pending.Add("Remaining não zerado");

        if (pending.Count > 0)
            throw DevOpsActionException.Conflict($"O card tem pendências: {string.Join(", ", pending)}");
    }

    private static string? GetString(AzureCardFullResponse card, string field) =>
        card.Fields.TryGetValue(field, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static decimal? GetNumber(AzureCardFullResponse card, string field) =>
        card.Fields.TryGetValue(field, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var v) ? v : null;

    private static decimal? ParseNumber(string? value) =>
        decimal.TryParse(value?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : null;

    private static string Format(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";
}
