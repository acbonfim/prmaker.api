using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>0078: uma sugestão do assistente do card.</summary>
public class ExecutionSuggestionResponse
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Text { get; set; }
    public List<ExecutionSuggestionAction> Actions { get; set; } = [];
    public string Status { get; set; } = string.Empty;
    public string? ChosenAction { get; set; }
    public string? Result { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public static ExecutionSuggestionResponse From(ExecutionSuggestion s) => new()
    {
        Id = s.Id, PlanId = s.PlanId, Kind = s.Kind, Title = s.Title, Text = s.Text, Actions = s.Actions, Status = s.Status,
        ChosenAction = s.ChosenAction, Result = s.Result, ResolvedBy = s.ResolvedBy, CreatedAt = s.CreatedAt, ResolvedAt = s.ResolvedAt
    };
}
