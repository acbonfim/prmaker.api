namespace solvace.github.domain.Responses;

/// <summary>Status atual de um PR no GitHub. Error preenchido quando não foi possível consultar.</summary>
public class PullRequestStatusResponse
{
    public string Repository { get; set; } = string.Empty;
    public int Number { get; set; }

    /// <summary>OPEN, MERGED ou CLOSED (ver PullRequestGithubStatus).</summary>
    public string Status { get; set; } = string.Empty;
    public bool IsDraft { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? Error { get; set; }
}
