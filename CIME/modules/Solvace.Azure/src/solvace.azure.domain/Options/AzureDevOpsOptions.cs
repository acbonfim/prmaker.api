namespace solvace.azure.domain.Options;

public class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    public string Organization { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string PersonalAccessToken { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "7.0";
    public string RootCauseFieldPath { get; set; } = "/fields/Custom.RCATechnicalCategorytext";
}