namespace solvace.azure.domain.Options;

/// <summary>
/// Nomes dos campos do work item que o PRMake lê e grava (feature 0030), configuráveis no plugin
/// "AzureDevOps Configurations" (campos fixos do admin). Sem valor, vale o padrão do processo atual.
/// </summary>
public static class AzureDevOpsFieldKeys
{
    public const string ResolutionType = "FieldResolutionType";
    public const string GeneralClassification = "FieldGeneralClassification";
    public const string Classification = "FieldClassification";
    public const string RemainingWork = "FieldRemainingWork";
    public const string OriginalEstimate = "FieldOriginalEstimate";
    public const string CompletedWork = "FieldCompletedWork";
    public const string RootCauseFieldPath = "RootCauseFieldPath";

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [ResolutionType] = "Custom.ResolutionType",
        [GeneralClassification] = "Custom.GeneralClassification",
        [Classification] = "Custom.Classification",
        [RemainingWork] = "Microsoft.VSTS.Scheduling.RemainingWork",
        [OriginalEstimate] = "Microsoft.VSTS.Scheduling.OriginalEstimate",
        [CompletedWork] = "Microsoft.VSTS.Scheduling.CompletedWork",
        [RootCauseFieldPath] = "/fields/Custom.RCATechnicalCategorytext"
    };
}

/// <summary>Nomes efetivos dos campos (configuração do usuário/admin com os padrões).</summary>
public record AzureDevOpsFieldNames(
    string ResolutionType,
    string GeneralClassification,
    string Classification,
    string RemainingWork,
    string OriginalEstimate,
    string CompletedWork,
    string RootCause);
