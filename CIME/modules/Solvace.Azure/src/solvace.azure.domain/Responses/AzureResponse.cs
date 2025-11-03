using System.Text.Json.Serialization;

namespace solvace.azure.domain.Models;

public class AzureWorkItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("rev")]
    public long Rev { get; set; }

    [JsonPropertyName("fields")]
    public WorkItemFields Fields { get; set; } = new();

    [JsonPropertyName("multilineFieldsFormat")]
    public MultilineFieldsFormat? MultilineFieldsFormat { get; set; }

    [JsonPropertyName("_links")]
    public WorkItemLinks Links { get; set; } = new();

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string? Error { get; set; } = string.Empty;
}

public class WorkItemFields
{
    [JsonPropertyName("System.AreaPath")]
    public string? AreaPath { get; set; }

    [JsonPropertyName("System.TeamProject")]
    public string? TeamProject { get; set; }

    [JsonPropertyName("System.IterationPath")]
    public string? IterationPath { get; set; }

    [JsonPropertyName("System.WorkItemType")]
    public string? WorkItemType { get; set; }

    [JsonPropertyName("System.State")]
    public string? State { get; set; }

    [JsonPropertyName("System.Reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("System.AssignedTo")]
    public AzureIdentity? AssignedTo { get; set; }

    [JsonPropertyName("System.CreatedDate")]
    public DateTime? CreatedDate { get; set; }

    [JsonPropertyName("System.CreatedBy")]
    public AzureIdentity? CreatedBy { get; set; }

    [JsonPropertyName("System.ChangedDate")]
    public DateTime? ChangedDate { get; set; }

    [JsonPropertyName("System.ChangedBy")]
    public AzureIdentity? ChangedBy { get; set; }

    [JsonPropertyName("System.CommentCount")]
    public long? CommentCount { get; set; }

    [JsonPropertyName("System.Title")]
    public string? Title { get; set; }

    [JsonPropertyName("System.BoardColumn")]
    public string? BoardColumn { get; set; }

    [JsonPropertyName("System.BoardColumnDone")]
    public bool? BoardColumnDone { get; set; }

    [JsonPropertyName("System.BoardLane")]
    public string? BoardLane { get; set; }

    [JsonPropertyName("System.Tags")]
    public string? Tags { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Scheduling.RemainingWork")]
    public decimal? RemainingWork { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Scheduling.OriginalEstimate")]
    public decimal? OriginalEstimate { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Scheduling.CompletedWork")]
    public decimal? CompletedWork { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Common.StateChangeDate")]
    public DateTime? StateChangeDate { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Common.ActivatedDate")]
    public DateTime? ActivatedDate { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Common.ActivatedBy")]
    public AzureIdentity? ActivatedBy { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Common.ResolvedDate")]
    public DateTime? ResolvedDate { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Common.ResolvedBy")]
    public AzureIdentity? ResolvedBy { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Common.Priority")]
    public long? Priority { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Common.Severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Common.StackRank")]
    public double? StackRank { get; set; }

    [JsonPropertyName("Microsoft.VSTS.Common.ValueArea")]
    public string? ValueArea { get; set; }

    [JsonPropertyName("Microsoft.VSTS.TCM.ReproSteps")]
    public string? ReproSteps { get; set; }

    [JsonPropertyName("Custom.Module")]
    public string? Module { get; set; }

    [JsonPropertyName("Custom.Platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("Custom.Site")]
    public string? Site { get; set; }

    [JsonPropertyName("Custom.CreationDate")]
    public DateTime? CreationDate { get; set; }

    [JsonPropertyName("Custom.PRtoReleaseCandidate")]
    public bool? PRtoReleaseCandidate { get; set; }

    [JsonPropertyName("Custom.Environment")]
    public string? Environment { get; set; }

    [JsonPropertyName("Custom.ReadTodo")]
    public bool? ReadTodo { get; set; }

    [JsonPropertyName("Custom.DeliveryDate")]
    public DateTime? DeliveryDate { get; set; }

    [JsonPropertyName("Custom.Foundby")]
    public string? Foundby { get; set; }

    [JsonPropertyName("Custom.TesterQA")]
    public AzureIdentity? TesterQA { get; set; }

    [JsonPropertyName("Custom.APIImpactValidation")]
    public string? APIImpactValidation { get; set; }

    [JsonPropertyName("Custom.BugReleaseorProduction")]
    public string? BugReleaseorProduction { get; set; }

    [JsonPropertyName("Custom.PriorityCS")]
    public bool? PriorityCS { get; set; }

    [JsonPropertyName("Custom.ValidationReturns")]
    public double? ValidationReturns { get; set; }

    [JsonPropertyName("Custom.BugType")]
    public string? BugType { get; set; }

    [JsonPropertyName("Custom.RCATechnicalCategorytext")]
    public string? RCATechnicalCategorytext { get; set; }

    [JsonPropertyName("WEF_B7FBB3D8E23D49AD9C358F55815C88C4_Kanban.Column")]
    public string? KanbanColumn { get; set; }

    [JsonPropertyName("WEF_B7FBB3D8E23D49AD9C358F55815C88C4_Kanban.Column.Done")]
    public bool? KanbanColumnDone { get; set; }

    [JsonPropertyName("WEF_B7FBB3D8E23D49AD9C358F55815C88C4_Kanban.Lane")]
    public string? KanbanLane { get; set; }

    [JsonPropertyName("WEF_8DE0623101BC40828BB20CA02DD24A2A_Kanban.Column")]
    public string? KanbanColumn2 { get; set; }

    [JsonPropertyName("WEF_8DE0623101BC40828BB20CA02DD24A2A_Kanban.Column.Done")]
    public bool? KanbanColumnDone2 { get; set; }

    [JsonPropertyName("WEF_8DE0623101BC40828BB20CA02DD24A2A_Kanban.Lane")]
    public string? KanbanLane2 { get; set; }
}

public class AzureIdentity
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("_links")]
    public IdentityLinks? Links { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("uniqueName")]
    public string UniqueName { get; set; } = string.Empty;

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("descriptor")]
    public string? Descriptor { get; set; }
}

public class IdentityLinks
{
    [JsonPropertyName("avatar")]
    public LinkReference? Avatar { get; set; }
}

public class LinkReference
{
    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;
}

public class MultilineFieldsFormat
{
    [JsonPropertyName("Microsoft.VSTS.TCM.ReproSteps")]
    public string? ReproSteps { get; set; }

    [JsonPropertyName("Custom.RCATechnicalCategorytext")]
    public string? RCATechnicalCategorytext { get; set; }
}

public class WorkItemLinks
{
    [JsonPropertyName("self")]
    public LinkReference? Self { get; set; }

    [JsonPropertyName("workItemUpdates")]
    public LinkReference? WorkItemUpdates { get; set; }

    [JsonPropertyName("workItemRevisions")]
    public LinkReference? WorkItemRevisions { get; set; }

    [JsonPropertyName("workItemComments")]
    public LinkReference? WorkItemComments { get; set; }

    [JsonPropertyName("html")]
    public LinkReference? Html { get; set; }

    [JsonPropertyName("workItemType")]
    public LinkReference? WorkItemType { get; set; }

    [JsonPropertyName("fields")]
    public LinkReference? Fields { get; set; }
}