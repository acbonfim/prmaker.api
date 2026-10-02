namespace solvace.azure.domain.Models;

/// <summary>Resumo de um card do DevOps para listas (home, 0051): título, estado, coluna do board e responsável.</summary>
public sealed class AzureCardSummaryResponse
{
    public long Id { get; set; }
    public string? Title { get; set; }
    public string? State { get; set; }
    public string? BoardColumn { get; set; }
    public string? WorkItemType { get; set; }
    public string? AssignedTo { get; set; }
    public string? AssignedToImageUrl { get; set; }
    public DateTime? ChangedDate { get; set; }
    public string Url { get; set; } = string.Empty;
}
