namespace solvace.prform.domain.Responses;

public class PullRequestRegisterResponse
{
    public int Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BranchPrefix { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string? RepositoryId { get; set; }

    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }

    /// <summary>PRs do GitHub do card (status persistido, sem consultar o GitHub), mais recentes primeiro.</summary>
    public List<PullRequestGithubResponse> GithubPullRequests { get; set; } = new();
}