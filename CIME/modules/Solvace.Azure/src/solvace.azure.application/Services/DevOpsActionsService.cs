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
    private const string UserStoryType = "User Story";

    /// <summary>
    /// Estado de pronto da User Story depois da validação em QA (o "Ready for QA" da US) quando o
    /// <c>UserStoryReadyForQaState</c> não está configurado — o estado em que as US de RCA validadas ficam.
    /// </summary>
    public const string DefaultUserStoryReadyForQaState = "In Development (done)";

    /// <summary>
    /// Opções padrão de classificação — as combinações usadas pelo time em 300 bugs resolvidos (estudo da 0027).
    /// Reserva: o valor efetivo é o do "AI Configurations" (BugClassificationPresets, semeado pela migração da 0030).
    /// Pública (0086): o catálogo das configurações mostra esta lista como o padrão da chave.
    /// </summary>
    public static readonly IReadOnlyList<DevOpsClassificationPreset> DefaultClassificationPresets =
    [
        Preset("code-fix", "Correção de código", "Code Fix", "Code", "Code Required - Code Defect", "A"),
        Preset("code-data-fix", "Código + correção de dados causada pelo defeito", "Code Fix", "Code", "Code Required - Data Fix / Request - Caused by Defect", "A+B"),
        Preset("script-defect", "Script de dados (causa: defeito)", "Configuration (Script)", "Code", "Code Required - Data Fix / Request - Caused by Defect", "B"),
        Preset("script-user-action", "Script de dados (causa: ação do usuário)", "Configuration (Script)", "No Code", "Code Required - Data Fix / Request - Caused by User Action", "B"),
        Preset("script-environment", "Script de ambiente/plataforma", "Configuration (Script)", "No Code", "No Code Required - Environment / Platform", "B/C"),
        Preset("configuration", "Configuração de ambiente/plataforma", "Configuration", "No Code", "No Code Required - Environment / Platform", "C/D"),
        Preset("configuration-change-request", "Configuração a pedido (change request)", "Configuration", "No Code", "No Code Required - Not a Defect - Change Request / Missed Requirement", "D"),
        Preset("user-education", "Orientação ao cliente (não é defeito)", "User Education", "No Code", "No Code Required - Not a Defect - Training", "E"),
        Preset("user-education-change-request", "Orientação + pedido de mudança", "User Education", "No Code", "No Code Required - Not a Defect - Change Request / Missed Requirement", "E/F"),
        Preset("change-request", "Change request", "Change Request", "No Code", "No Code Required - Not a Defect - Change Request / Missed Requirement", "F"),
        Preset("not-mapped-requirement", "Requisito não mapeado", "Not Mapped Requirement", "No Code", "No Code Required - Not a Defect - Change Request / Missed Requirement", "F"),
        Preset("cannot-reproduce", "Não reproduz", "Cannot reproduce", "No Code", "No Code Required - Environment / Platform", "G"),
        Preset("no-user-feedback", "Sem retorno do cliente", "Cannot reproduce", "No user feedback", "No user feedback - Pending information", "G"),
        Preset("duplicated", "Duplicado", "Duplicated", "Duplicated", "Ticket duplicated", "H")
    ];

    private static DevOpsClassificationPreset Preset(string key, string label, string resolutionType, string general, string classification, string pattern) =>
        new() { Key = key, Label = label, ResolutionType = resolutionType, GeneralClassification = general, Classification = classification, Pattern = pattern };

    private readonly IAzureService _azureService;
    private readonly IPluginConfigurationResolver _configurationResolver;
    private readonly IPullRequestApplication _pullRequestApplication;

    private readonly ISpecImageSource? _specImages;

    public DevOpsActionsService(IAzureService azureService, IPluginConfigurationResolver configurationResolver,
        IPullRequestApplication pullRequestApplication, ISpecImageSource? specImages = null)
    {
        _specImages = specImages;
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
                DevTestInQa = new DevOpsDevTestInQaConfig
                {
                    State = Value(AIConfigurationKeys.BugDevTestInQaState),
                    Column = Value(AIConfigurationKeys.BugDevTestInQaColumn)
                },
                InitialEstimate = new DevOpsInitialEstimateConfig
                {
                    Configured = original is not null && remaining is not null && completed is not null,
                    OriginalEstimate = original,
                    RemainingWork = remaining,
                    CompletedWork = completed
                }
            },
            UserStory = new DevOpsUserStoryActionsConfig
            {
                ReadyForQa = new DevOpsReadyForQaConfig { State = Value(AIConfigurationKeys.UserStoryReadyForQaState) ?? DefaultUserStoryReadyForQaState }
            }
        };
    }

    public async Task<DevOpsActionResponse> MoveToTestInProductionAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var bug = (await GetConfigAsync(cancellationToken)).Bug.TestInProduction;
        if (string.IsNullOrEmpty(bug.Area) || string.IsNullOrEmpty(bug.State))
            throw DevOpsActionException.Conflict("Área e estado de destino não configurados no plugin AI Configurations");

        var card = await LoadBugAsync(cardNumber, cancellationToken);
        await EnsureNoPendenciesAsync(card, cancellationToken);

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

    /// <summary>
    /// Bug: estado <c>BugReadyForQaState</c>. User Story (fechamento do RCA): <c>UserStoryReadyForQaState</c> (padrão
    /// <see cref="DefaultUserStoryReadyForQaState"/>). Nos dois, só sem pendências de fechamento.
    /// </summary>
    public async Task<DevOpsActionResponse> MoveToReadyForQaAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var card = await LoadCardAsync(cardNumber, cancellationToken);
        var config = await GetConfigAsync(cancellationToken);
        var state = IsUserStory(card) ? config.UserStory.ReadyForQa.State : config.Bug.ReadyForQa.State;
        if (string.IsNullOrEmpty(state))
            throw DevOpsActionException.Conflict("Estado de Ready for QA não configurado no plugin AI Configurations");

        await EnsureNoPendenciesAsync(card, cancellationToken);

        var rev = await _azureService.PatchFieldsAsync(cardNumber, new Dictionary<string, object> { [FieldState] = state }, cancellationToken);
        return new DevOpsActionResponse { Rev = rev, Message = $"Card movido para {state} (Ready for QA)" };
    }

    /// <summary>
    /// Dev validando a correção em QA: estado + coluna do board (campo <c>WEF_…_Kanban.Column</c> do board do time
    /// do card). Sem checar pendências — o card ainda não foi fechado; o Ready for QA vem depois da validação.
    /// </summary>
    public async Task<DevOpsActionResponse> MoveToDevTestInQaAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var target = (await GetConfigAsync(cancellationToken)).Bug.DevTestInQa;
        if (string.IsNullOrEmpty(target.State) && string.IsNullOrEmpty(target.Column))
            throw DevOpsActionException.Conflict("Estado/coluna de Dev Test in QA não configurados no plugin AI Configurations");

        var card = await LoadBugAsync(cardNumber, cancellationToken);
        var fields = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(target.State))
            fields[FieldState] = target.State;
        if (!string.IsNullOrEmpty(target.Column))
        {
            var columnField = BoardColumnField(card)
                ?? throw DevOpsActionException.Conflict("O card não está em nenhum board do DevOps — não dá para mudar a coluna");
            fields[columnField] = target.Column;
        }

        var rev = await _azureService.PatchFieldsAsync(cardNumber, fields, cancellationToken);
        var where = string.Join(" · ", new[] { target.State, string.IsNullOrEmpty(target.Column) ? null : $"coluna {target.Column}" }.Where(s => !string.IsNullOrEmpty(s)));
        return new DevOpsActionResponse { Rev = rev, Message = $"Card movido para {where} (Dev Test in QA)" };
    }

    /// <summary>
    /// Campo da coluna do board em que o card aparece: cada board tem o seu <c>WEF_&lt;id&gt;_Kanban.Column</c>; o do time
    /// do card é o que tem <c>WEF_&lt;id&gt;_System.ExtensionMarker = true</c> (empate: o que bate com System.BoardColumn).
    /// </summary>
    private static string? BoardColumnField(AzureCardFullResponse card)
    {
        const string suffix = "_Kanban.Column";
        var candidates = card.Fields.Keys
            .Where(k => k.StartsWith("WEF_", StringComparison.Ordinal) && k.EndsWith(suffix, StringComparison.Ordinal))
            .Select(k => new
            {
                Field = k,
                Active = card.Fields.TryGetValue(k[..^suffix.Length] + "_System.ExtensionMarker", out var m) && m.ValueKind == JsonValueKind.True,
                Current = GetString(card, k)
            })
            .ToList();
        var boardColumn = GetString(card, "System.BoardColumn");
        return candidates.OrderByDescending(c => c.Active)
            .ThenByDescending(c => string.Equals(c.Current, boardColumn, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()?.Field;
    }

    public async Task<DevOpsActionResponse> SetInitialEstimateAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var estimate = (await GetConfigAsync(cancellationToken)).Bug.InitialEstimate;
        if (!estimate.Configured)
            throw DevOpsActionException.Conflict("Preencha os valores da estimativa inicial em Minhas integrações → AI Configurations");

        var card = await LoadBugAsync(cardNumber, cancellationToken);
        var names = await _azureService.GetFieldNamesAsync(cancellationToken);
        if (GetNumber(card, names.OriginalEstimate) is { } current && current != 0m)
            throw DevOpsActionException.Conflict($"O card já tem Original Estimate ({current.ToString(CultureInfo.InvariantCulture)})");

        var rev = await _azureService.PatchFieldsAsync(cardNumber, new Dictionary<string, object>
        {
            [names.OriginalEstimate] = estimate.OriginalEstimate!.Value,
            [names.RemainingWork] = estimate.RemainingWork!.Value,
            [names.CompletedWork] = estimate.CompletedWork!.Value
        }, cancellationToken);

        return new DevOpsActionResponse
        {
            Rev = rev,
            Message = "Estimativa inicial registrada: " +
                      $"Original Estimate {Format(estimate.OriginalEstimate)}, Remaining Work {Format(estimate.RemainingWork)}, Completed Work {Format(estimate.CompletedWork)}"
        };
    }

    /// <summary>Bug e User Story (fechamento do RCA da US).</summary>
    public async Task<DevOpsActionResponse> ZeroRemainingAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var card = await LoadCardAsync(cardNumber, cancellationToken);
        var names = await _azureService.GetFieldNamesAsync(cancellationToken);
        var remaining = GetNumber(card, names.RemainingWork);
        if (remaining is null or 0m)
            throw DevOpsActionException.Conflict("O Remaining Work do card já está zerado");

        var rev = await _azureService.PatchFieldsAsync(cardNumber, new Dictionary<string, object> { [names.RemainingWork] = 0 }, cancellationToken);
        return new DevOpsActionResponse { Rev = rev, Message = $"Remaining Work zerado (era {Format(remaining)})" };
    }

    public async Task<DevOpsActionResponse> SetStateAsync(string cardNumber, string state, string? reason, CancellationToken cancellationToken = default)
    {
        var target = state?.Trim();
        if (string.IsNullOrEmpty(target))
            throw DevOpsActionException.Conflict("Informe o estado de destino.");
        // Vale para Bug e User Story (o desfazer de um item de trabalho volta o estado de qualquer card).
        var card = await _azureService.GetCardFullAsync(cardNumber, cancellationToken);
        if (card is null || !string.IsNullOrEmpty(card.Error) || card.Id == 0)
            throw DevOpsActionException.NotFound($"Card {cardNumber} não encontrado no DevOps");
        var type = GetString(card, FieldWorkItemType) ?? "Bug";
        var states = await _azureService.GetWorkItemTypeStatesAsync(type, cancellationToken);
        var valid = states.FirstOrDefault(s => string.Equals(s, target, StringComparison.OrdinalIgnoreCase))
                    ?? throw DevOpsActionException.Conflict($"\"{target}\" não é um estado de {type}. Estados válidos: {string.Join(", ", states)}");
        var current = GetString(card, FieldState);
        if (string.Equals(current, valid, StringComparison.OrdinalIgnoreCase))
            throw DevOpsActionException.Conflict($"O card já está em {valid}");

        var fields = new Dictionary<string, object> { [FieldState] = valid };
        if (!string.IsNullOrWhiteSpace(reason))
            fields[FieldHistory] = WebUtility.HtmlEncode(reason.Trim()); // comentário na Discussion, na mesma revisão
        var rev = await _azureService.PatchFieldsAsync(cardNumber, fields, cancellationToken);
        return new DevOpsActionResponse { Rev = rev, Message = $"Card movido de {current ?? "—"} para {valid}" };
    }

    public async Task<CardStatesResponse> GetCardStatesAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var card = await LoadCardAsync(cardNumber, cancellationToken);
        var type = GetString(card, FieldWorkItemType) ?? "Bug";
        var states = await _azureService.GetWorkItemTypeStateInfosAsync(type, cancellationToken);
        var current = GetString(card, FieldState);

        // Ações do menu "Ações DevOps": os estados configurados pelo usuário são os próximos usuais (US: só o Ready for QA).
        var actions = new List<(string Action, string? State)>();
        try
        {
            var config = await GetConfigAsync(cancellationToken);
            if (string.Equals(type, UserStoryType, StringComparison.OrdinalIgnoreCase))
                actions.Add((CardStateSuggestions.ReadyForQa, config.UserStory.ReadyForQa.State));
            else
            {
                actions.Add((CardStateSuggestions.DevTestInQa, config.Bug.DevTestInQa.State));
                actions.Add((CardStateSuggestions.ReadyForQa, config.Bug.ReadyForQa.State));
                actions.Add((CardStateSuggestions.TestInProduction, config.Bug.TestInProduction.State));
            }
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            // Sem "AI Configurations": sugere pela ordem/categoria do DevOps.
        }

        return new CardStatesResponse
        {
            Type = type,
            Current = current,
            States = CardStateSuggestions.Build(states, current, actions)
        };
    }

    public async Task<DevOpsEffortResponse> GetEffortAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var card = await LoadCardAsync(cardNumber, cancellationToken);
        var names = await _azureService.GetFieldNamesAsync(cancellationToken);
        return new DevOpsEffortResponse
        {
            OriginalEstimate = GetNumber(card, names.OriginalEstimate),
            CompletedWork = GetNumber(card, names.CompletedWork),
            RemainingWork = GetNumber(card, names.RemainingWork),
        };
    }

    public async Task<DevOpsActionResponse> LogHoursAsync(string cardNumber, decimal hours, CancellationToken cancellationToken = default)
    {
        if (hours is <= 0 or > 24)
            throw DevOpsActionException.Conflict("Informe de 0,5 a 24 horas.");
        var card = await LoadCardAsync(cardNumber, cancellationToken);
        var names = await _azureService.GetFieldNamesAsync(cancellationToken);
        var completed = GetNumber(card, names.CompletedWork) ?? 0m;
        var remaining = GetNumber(card, names.RemainingWork) ?? 0m;
        var newCompleted = completed + hours;
        var newRemaining = Math.Max(0m, remaining - hours);
        var rev = await _azureService.PatchFieldsAsync(cardNumber, new Dictionary<string, object>
        {
            [names.CompletedWork] = newCompleted,
            [names.RemainingWork] = newRemaining
        }, cancellationToken);
        return new DevOpsActionResponse
        {
            Rev = rev,
            Message = $"Horas lançadas: +{Format(hours)}h — Completed Work {Format(completed)} → {Format(newCompleted)}, Remaining Work {Format(remaining)} → {Format(newRemaining)}"
        };
    }

    public async Task<IReadOnlyList<DevOpsClassificationPreset>> GetClassificationPresetsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _configurationResolver.GetEffectiveConfigurationAsync(AIConfigurationKeys.PluginName, cancellationToken);
            var json = config.GetConfigurationValueOrDefault(AIConfigurationKeys.BugClassificationPresets, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var custom = System.Text.Json.JsonSerializer.Deserialize<List<DevOpsClassificationPreset>>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var valid = custom?.Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.ResolutionType)
                                               && !string.IsNullOrWhiteSpace(p.GeneralClassification) && !string.IsNullOrWhiteSpace(p.Classification)).ToList();
                if (valid is { Count: > 0 })
                    return valid;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException or System.Text.Json.JsonException)
        {
            // Plugin ausente, não configurado pelo usuário ou JSON inválido: usa as opções padrão.
        }
        return DefaultClassificationPresets;
    }

    public async Task<IReadOnlyList<string>> GetBugTypesAsync(CancellationToken cancellationToken = default)
    {
        var names = await _azureService.GetFieldNamesAsync(cancellationToken);
        return await _azureService.GetFieldAllowedValuesAsync("Bug", names.BugType, cancellationToken) ?? [];
    }

    public async Task<DevOpsActionResponse> ClassifyAsync(string cardNumber, ClassifyCardRequest request, CancellationToken cancellationToken = default)
    {
        string? resolution = request.ResolutionType?.Trim(), general = request.GeneralClassification?.Trim(), classification = request.Classification?.Trim();
        var bugType = request.BugType?.Trim();
        if (!string.IsNullOrWhiteSpace(request.Preset))
        {
            var presets = await GetClassificationPresetsAsync(cancellationToken);
            var preset = presets.FirstOrDefault(p => string.Equals(p.Key, request.Preset.Trim(), StringComparison.OrdinalIgnoreCase))
                         ?? throw new DevOpsActionException(400, $"Classificação desconhecida: '{request.Preset}'. Opções: {string.Join(", ", presets.Select(p => p.Key))}");
            resolution = preset.ResolutionType;
            general = preset.GeneralClassification;
            classification = preset.Classification;
        }
        var anyClassification = !string.IsNullOrWhiteSpace(resolution) || !string.IsNullOrWhiteSpace(general) || !string.IsNullOrWhiteSpace(classification);
        var fullClassification = !string.IsNullOrWhiteSpace(resolution) && !string.IsNullOrWhiteSpace(general) && !string.IsNullOrWhiteSpace(classification);
        if (anyClassification ? !fullClassification : string.IsNullOrWhiteSpace(bugType))
            throw new DevOpsActionException(400, "Informe o preset ou os três valores (resolutionType, generalClassification e classification) e/ou o bugType");

        // Bug e User Story (fechamento do RCA da US: mesmos campos de classificação); Bug Type só existe no Bug.
        var card = await LoadCardAsync(cardNumber, cancellationToken);
        if (!string.IsNullOrWhiteSpace(bugType) && IsUserStory(card))
            throw new DevOpsActionException(400, "Bug Type é só do Bug — a User Story não tem o campo; classifique sem bugType");
        var names = await _azureService.GetFieldNamesAsync(cancellationToken);
        var fields = new Dictionary<string, object>();
        if (fullClassification)
        {
            fields[names.ResolutionType] = resolution!;
            fields[names.GeneralClassification] = general!;
            fields[names.Classification] = classification!;
        }
        if (!string.IsNullOrWhiteSpace(bugType))
        {
            // Bug Type é picklist do DevOps: só aceita um valor da lista (com a grafia de lá).
            var options = await _azureService.GetFieldAllowedValuesAsync("Bug", names.BugType, cancellationToken)
                          ?? throw new DevOpsActionException(400, $"O Bug do DevOps não tem o campo {names.BugType} (Bug Type)");
            bugType = options.FirstOrDefault(o => string.Equals(o, bugType, StringComparison.OrdinalIgnoreCase))
                      ?? (options.Count == 0 ? bugType
                          : throw new DevOpsActionException(400, $"Bug Type desconhecido: '{bugType}'. Opções: {string.Join(" | ", options)}"));
            fields[names.BugType] = bugType;
        }

        var rev = await _azureService.PatchFieldsAsync(cardNumber, fields, cancellationToken);
        var parts = new List<string>();
        if (fullClassification) parts.Add($"Classificação do card: {resolution} · {general} · {classification}");
        if (!string.IsNullOrWhiteSpace(bugType)) parts.Add($"Bug Type: {bugType}");
        return new DevOpsActionResponse { Rev = rev, Message = string.Join(" · ", parts) };
    }

    public async Task<PullRequestRegisterResponse> SaveSummaryAsync(string cardNumber, SaveSummaryRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Summary))
            throw new DomainException("Resumo vazio");
        if (request.Publish && string.IsNullOrWhiteSpace(request.Html))
            throw new DomainException("Resumo em HTML vazio: informe o html para publicar na discussion");

        var register = await _pullRequestApplication.GetByCardNumber(cardNumber, cancellationToken);
        if (register is null)
        {
            // Card tratado só pela Bancada não tem registro (a tela de PR saiu): publica sem gravar o resumo.
            if (!request.Publish)
                throw new DomainException($"O card {cardNumber} não tem registro no PRMake para guardar o resumo: publique na discussion");
            var newCommentId = await _azureService.UpsertCommentAsync(cardNumber.Trim(), request.Html!, null, cancellationToken);
            return new PullRequestRegisterResponse
            {
                CardNumber = cardNumber.Trim(),
                Summary = request.Summary,
                SummaryCommentId = newCommentId,
                SummaryPublishedAt = DateTimeOffset.UtcNow
            };
        }

        if (!request.Publish)
            return await _pullRequestApplication.SaveSummary(register.CardNumber, request.Summary, null, cancellationToken);

        // Primeiro publica: se o DevOps falhar, nada é gravado no PRMake.
        var commentId = await _azureService.UpsertCommentAsync(register.CardNumber, request.Html!, register.SummaryCommentId, cancellationToken);
        return await _pullRequestApplication.SaveSummary(register.CardNumber, request.Summary, commentId, cancellationToken);
    }

    private const int MaxCommentImages = 10;
    private const int MaxCommentImageBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> CommentImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp" };

    public async Task<DevOpsCommentResponse> PostCommentAsync(string cardNumber, PostCommentRequest request, CancellationToken cancellationToken = default)
    {
        var card = cardNumber?.Trim() ?? string.Empty;
        var images = request.Images ?? [];
        if (string.IsNullOrWhiteSpace(request.Html) && images.Count == 0)
            throw new DevOpsActionException(400, "Comentário vazio: informe o html");
        if (images.Count > MaxCommentImages)
            throw new DevOpsActionException(400, $"No máximo {MaxCommentImages} imagens por comentário");

        // Valida tudo antes de subir qualquer anexo.
        var files = new List<(string Name, byte[] Content)>();
        foreach (var image in images)
        {
            var name = Path.GetFileName(image.FileName?.Trim() ?? string.Empty);
            if (name.Length == 0 || !CommentImageExtensions.Contains(Path.GetExtension(name)))
                throw new DevOpsActionException(400, $"Imagem inválida: '{image.FileName}' (aceitas: {string.Join(", ", CommentImageExtensions)})");
            byte[] content;
            try { content = Convert.FromBase64String(image.ContentBase64 ?? string.Empty); }
            catch (FormatException) { throw new DevOpsActionException(400, $"Imagem {name}: conteúdo não está em base64"); }
            if (content.Length == 0 || content.Length > MaxCommentImageBytes)
                throw new DevOpsActionException(400, $"Imagem {name}: vazia ou maior que {MaxCommentImageBytes / 1024 / 1024} MB");
            files.Add((name, content));
        }

        var html = new System.Text.StringBuilder(request.Html?.Trim() ?? string.Empty);
        foreach (var (name, content) in files)
        {
            var url = await _azureService.UploadAttachmentAsync(name, content, cancellationToken);
            html.Append($"<p><img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"{WebUtility.HtmlEncode(name)}\"></p>");
        }

        var commentId = await _azureService.UpsertCommentAsync(card, html.ToString(), null, cancellationToken);
        return new DevOpsCommentResponse
        {
            CommentId = commentId,
            Images = files.Count,
            Message = files.Count > 0
                ? $"Comentário publicado na discussion do card com {files.Count} imagem(ns)"
                : "Comentário publicado na discussion do card"
        };
    }

    /// <summary>
    /// Relê o card no DevOps, de qualquer tipo — horas (Original/Completed/Remaining) e lançamento valem para Bug e
    /// User Story (0085: a Bancada mostra as horas da US).
    /// </summary>
    public async Task<IReadOnlyList<SpecSectionResponse>> GetSpecAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var card = await LoadUserStoryAsync(cardNumber, cancellationToken);
        var map = await _azureService.GetUserStorySpecFieldsAsync(cancellationToken);
        return map.Select(m => new SpecSectionResponse { Section = m.Section, Field = m.Field, Text = PlainText(GetString(card, m.Field)) }).ToList();
    }

    public async Task<DevOpsActionResponse> WriteSpecAsync(string cardNumber, WriteSpecRequest request, CancellationToken cancellationToken = default)
    {
        await LoadUserStoryAsync(cardNumber, cancellationToken);
        var map = await _azureService.GetUserStorySpecFieldsAsync(cancellationToken);
        if (map.Count == 0)
            throw DevOpsActionException.Conflict("Nenhuma seção da especificação configurada (Azure DevOps → Campos do card → Especificação da User Story).");

        var sections = new List<string>();
        var fields = new Dictionary<string, object>(StringComparer.Ordinal);
        void Put(string section, string field, string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return;
            fields[field] = UserStorySpec.ToHtml(markdown);
            sections.Add(section);
        }

        if (!string.IsNullOrWhiteSpace(request.Document))
            foreach (var (section, text) in UserStorySpec.Split(request.Document, map))
                Put(section, map.First(m => m.Section == section).Field, text);

        foreach (var (key, value) in request.Fields ?? [])
        {
            if (string.IsNullOrWhiteSpace(key) || value is null) continue;
            var bySection = UserStorySpec.MatchSection(key, map);
            var byField = map.FirstOrDefault(m => string.Equals(m.Field, key.Trim(), StringComparison.OrdinalIgnoreCase));
            if (bySection is not null)
                Put(bySection, map.First(m => m.Section == bySection).Field, value);
            else if (byField.Field is not null)
                Put(byField.Section, byField.Field, value);
            else if (key.Contains('.') && !key.Trim().Contains(' '))
            {
                fields[key.Trim()] = value.Trim(); // outro campo do card (ex.: picklist): vai como veio
                sections.Add(key.Trim());
            }
            else
                throw DevOpsActionException.Conflict($"\"{key}\" não é seção da especificação nem campo do card. Seções: {string.Join(", ", map.Select(m => m.Section))}.");
        }

        if (fields.Count == 0)
            throw DevOpsActionException.Conflict("Nada para gravar: o documento precisa de uma linha de título por seção (" +
                                                 string.Join(", ", map.Select(m => m.Section)) + ") com o texto embaixo.");
        var images = await UploadSpecImagesAsync(cardNumber, request.FilesFrom, fields, cancellationToken);
        var rev = await _azureService.PatchFieldsAsync(cardNumber, fields, cancellationToken);
        var message = $"Especificação da US gravada no card: {string.Join(", ", sections.Distinct())}";
        return new DevOpsActionResponse { Rev = rev, Message = images > 0 ? $"{message} ({images} imagem(ns))" : message };
    }

    /// <summary>
    /// Card 78525: <c>anexo:nome</c> não abre no DevOps — cada imagem é achada nos arquivos dos cards (o próprio e os de
    /// <paramref name="filesFrom"/>), sobe como anexo do work item e o texto aponta para ela. Faltou alguma: recusa antes
    /// de gravar (nada de imagem quebrada no card).
    /// </summary>
    private async Task<int> UploadSpecImagesAsync(string cardNumber, IEnumerable<string>? filesFrom, Dictionary<string, object> fields,
        CancellationToken cancellationToken)
    {
        var names = fields.Values.OfType<string>().SelectMany(UserStorySpec.AttachmentImages).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) return 0;
        var cards = new[] { cardNumber }.Concat(filesFrom ?? []).Select(c => c?.Trim().TrimStart('#') ?? string.Empty)
            .Where(c => c.Length > 0).Distinct().ToList();
        if (_specImages is null)
            throw DevOpsActionException.Conflict("Imagens anexo: não podem ser resolvidas aqui — envie o texto sem elas.");

        var found = new List<(string Name, byte[] Content)>();
        var missing = new List<string>();
        foreach (var name in names)
        {
            if (await _specImages.FindAsync(cards, name, cancellationToken) is { } file && file.Content.Length > 0)
                found.Add((name, file.Content));
            else
                missing.Add(name);
        }
        if (missing.Count > 0)
            throw DevOpsActionException.Conflict($"Imagem não encontrada nos arquivos do(s) card(s) {string.Join(", ", cards)}: {string.Join(", ", missing)}. " +
                                                 "Informe em filesFrom o card onde as imagens foram gravadas (ex.: o card da análise).");

        foreach (var (name, content) in found)
        {
            var url = await _azureService.UploadAttachmentAsync(name, content, cancellationToken);
            foreach (var key in fields.Keys.ToList())
                if (fields[key] is string html)
                    fields[key] = UserStorySpec.ReplaceAttachment(html, name, url);
        }
        return found.Count;
    }

    private async Task<AzureCardFullResponse> LoadUserStoryAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var card = await LoadCardAsync(cardNumber, cancellationToken);
        if (!string.Equals(GetString(card, FieldWorkItemType), "User Story", StringComparison.OrdinalIgnoreCase))
            throw DevOpsActionException.Conflict($"A especificação é da User Story — o card {cardNumber} é {GetString(card, FieldWorkItemType) ?? "outro tipo"}.");
        return card;
    }

    private static string? PlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<(br|/p|/li|/h[1-6]|/tr)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty));
        return System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
    }

    private async Task<AzureCardFullResponse> LoadCardAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var card = await _azureService.GetCardFullAsync(cardNumber, cancellationToken);
        if (card is null || !string.IsNullOrEmpty(card.Error) || card.Id == 0)
            throw DevOpsActionException.NotFound($"Card {cardNumber} não encontrado no DevOps");
        return card;
    }

    /// <summary>
    /// Relê o card no DevOps; só cards do tipo Bug (Test in production, Dev Test in QA e estimativa inicial). As ações do
    /// fechamento da User Story (classificação, Remaining, Ready for QA) usam <see cref="LoadCardAsync"/>.
    /// </summary>
    private async Task<AzureCardFullResponse> LoadBugAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var card = await LoadCardAsync(cardNumber, cancellationToken);

        // Mesma regra do front: User Story é US, o resto é tratado como Bug.
        if (IsUserStory(card))
            throw DevOpsActionException.Conflict("Esta ação é só do Bug — na User Story o fechamento é classificação, Remaining zerado e Ready for QA");

        return card;
    }

    private static bool IsUserStory(AzureCardFullResponse card) =>
        string.Equals(GetString(card, FieldWorkItemType), UserStoryType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// O tipo do card tem o campo de root cause? Bug sempre; User Story só se o processo do DevOps tiver o campo nela
    /// (hoje não tem: o RCA da US vai como comentário na discussion).
    /// </summary>
    private async Task<bool> HasRootCauseFieldAsync(AzureCardFullResponse card, CancellationToken cancellationToken)
    {
        if (!IsUserStory(card))
            return true;
        var names = await _azureService.GetFieldNamesAsync(cancellationToken);
        return await _azureService.GetFieldAllowedValuesAsync(UserStoryType, names.RootCause, cancellationToken) is not null;
    }

    public async Task EnsureRootCauseFieldAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var card = await LoadCardAsync(cardNumber, cancellationToken);
        if (!await HasRootCauseFieldAsync(card, cancellationToken))
            throw DevOpsActionException.Conflict(
                "A User Story não tem o campo de root cause no DevOps: publique o RCA (no Modelo do RCA do prompt) como comentário na discussion do card");
    }

    private async Task EnsureNoPendenciesAsync(AzureCardFullResponse card, CancellationToken cancellationToken)
    {
        var a = card.Alerts;
        var pending = new List<string>();
        if (a.MissingRootCause && await HasRootCauseFieldAsync(card, cancellationToken)) pending.Add("Root Cause");
        if (a.MissingResolutionType) pending.Add("Resolution Type");
        if (a.MissingGeneralClassification) pending.Add("General Classification");
        if (a.MissingClassification) pending.Add("Classification");
        if (a.MissingBugType) pending.Add("Bug Type");
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
