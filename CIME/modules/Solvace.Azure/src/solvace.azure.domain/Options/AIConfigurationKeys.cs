namespace solvace.azure.domain.Options;

/// <summary>
/// Chaves das ações de DevOps no plugin "AI Configurations" (feature 0011). Prefixo "Bug" = ações do Bug; "UserStory" =
/// as da User Story (fechamento do RCA).
/// </summary>
public static class AIConfigurationKeys
{
    public const string PluginName = "AI Configurations";

    public const string BugSummaryPrompt = "BugSummaryPrompt";
    public const string BugTestInProductionRequiredArea = "BugTestInProductionRequiredArea";
    public const string BugTestInProductionArea = "BugTestInProductionArea";
    public const string BugTestInProductionState = "BugTestInProductionState";
    public const string BugTestInProductionComment = "BugTestInProductionComment";

    /// <summary>
    /// 0099: coluna do board ao mover para Test in production — o estado (ex.: Test QA) e a coluna (ex.: QA Done) são coisas
    /// diferentes no DevOps. Vazio = só o estado (o card cai na primeira coluna do estado).
    /// </summary>
    public const string BugTestInProductionColumn = "BugTestInProductionColumn";
    public const string BugReadyForQaState = "BugReadyForQaState";

    /// <summary>
    /// Fechamento da User Story (US de RCA): estado de pronto depois da validação em QA (o Ready for QA da US). Vazio = o
    /// padrão do código (<c>DevOpsActionsService.DefaultUserStoryReadyForQaState</c>).
    /// </summary>
    public const string UserStoryReadyForQaState = "UserStoryReadyForQaState";

    /// <summary>
    /// "Dev Test in QA" (dev validando a correção em QA, antes do Ready for QA): estado e coluna do board de destino.
    /// Coluna vazia = só o estado.
    /// </summary>
    public const string BugDevTestInQaState = "BugDevTestInQaState";
    public const string BugDevTestInQaColumn = "BugDevTestInQaColumn";
    public const string BugInitialOriginalEstimate = "BugInitialOriginalEstimate";
    public const string BugInitialRemainingWork = "BugInitialRemainingWork";
    public const string BugInitialCompletedWork = "BugInitialCompletedWork";

    /// <summary>
    /// Opções de classificação do card (JSON: [{key,label,resolutionType,generalClassification,classification,pattern}]).
    /// Vazio = as opções padrão, tiradas das resoluções reais (feature 0028).
    /// </summary>
    public const string BugClassificationPresets = "BugClassificationPresets";
}
