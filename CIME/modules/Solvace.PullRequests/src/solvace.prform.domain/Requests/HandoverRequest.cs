namespace solvace.prform.domain.Requests;

public class HandoverRequest
{
    public string CardNumber { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? RepositoryId { get; set; }
}
