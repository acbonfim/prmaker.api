namespace solvace.prform.domain.Responses;

public class HandoverRecentResponse
{
    public int Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string? RepositoryId { get; set; }

    /// <summary>ExternalId de quem criou o handover (armazenado em CreatedBy).</summary>
    public string? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
