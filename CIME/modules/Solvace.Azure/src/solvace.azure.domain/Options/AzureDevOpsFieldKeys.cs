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
    public const string BugType = "FieldBugType";
    /// <summary>
    /// Card 78525: seções da especificação da User Story (refinamento) → campo do work item, na ordem do formulário. JSON
    /// {"Seção": "Campo"}; a seção é o título que a análise usa no documento (o rótulo do formulário pode ser outro).
    /// </summary>
    public const string UserStorySpecFields = "FieldsUserStorySpec";

    public static readonly string DefaultUserStorySpecFieldsJson =
        "{\"High Level Business Specification (PM)\":\"System.Description\"," +
        "\"Detailed Business Specification (PO)\":\"Custom.DetailedBusinessSpecification\"," +
        "\"Specification Main Highlights\":\"Custom.SpecificationMainHighlights\"," +
        "\"Technical Specification (Tech Lead / Architect)\":\"Custom.TechnicalSpecification\"," +
        "\"Main Validation Scenarios (QA)\":\"Microsoft.VSTS.Common.AcceptanceCriteria\"}";

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [ResolutionType] = "Custom.ResolutionType",
        [GeneralClassification] = "Custom.GeneralClassification",
        [Classification] = "Custom.Classification",
        [RemainingWork] = "Microsoft.VSTS.Scheduling.RemainingWork",
        [OriginalEstimate] = "Microsoft.VSTS.Scheduling.OriginalEstimate",
        [CompletedWork] = "Microsoft.VSTS.Scheduling.CompletedWork",
        [RootCauseFieldPath] = "/fields/Custom.RCATechnicalCategorytext",
        [BugType] = "Custom.BugType",
        [UserStorySpecFields] = DefaultUserStorySpecFieldsJson
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
    string RootCause,
    string BugType);
