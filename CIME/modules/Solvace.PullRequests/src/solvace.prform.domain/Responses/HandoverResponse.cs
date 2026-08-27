namespace solvace.prform.domain.Responses;

public class HandoverResponse
{
    public int Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string? RepositoryId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
