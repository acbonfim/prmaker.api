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
    public const string BugInitialOriginalEstimate = "BugInitialOriginalEstimate";
    public const string BugInitialRemainingWork = "BugInitialRemainingWork";
    public const string BugInitialCompletedWork = "BugInitialCompletedWork";
}
