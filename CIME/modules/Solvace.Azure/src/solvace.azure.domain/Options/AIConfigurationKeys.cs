namespace solvace.azure.domain.Options;

/// <summary>
/// Chaves das ações de DevOps no plugin "AI Configurations" (feature 0011). O prefixo "Bug" deixa
/// espaço para as configurações de User Story, que virão depois.
/// </summary>
public static class AIConfigurationKeys
{
    public const string PluginName = "AI Configurations";

    public const string BugSummaryPrompt = "BugSummaryPrompt";
    public const string BugTestInProductionRequiredArea = "BugTestInProductionRequiredArea";
    public const string BugTestInProductionArea = "BugTestInProductionArea";
    public const string BugTestInProductionState = "BugTestInProductionState";
    public const string BugTestInProductionComment = "BugTestInProductionComment";
    public const string BugReadyForQaState = "BugReadyForQaState";

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
