namespace solvace.timeline.domain.Responses;

public class TimelineEntryResponse
{
    public int Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? SourceMessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
