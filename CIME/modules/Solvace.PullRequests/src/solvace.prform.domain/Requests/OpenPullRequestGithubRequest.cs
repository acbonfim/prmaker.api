namespace solvace.prform.domain.Requests;

/// <summary>Abre um PR no GitHub para o card (POST /PullRequest/{cardNumber}/github).</summary>
public class OpenPullRequestGithubRequest
{
    public string RepositoryId { get; set; } = string.Empty;
    public string BranchPrefix { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Draft { get; set; }
    public Guid UserId { get; set; }
}
