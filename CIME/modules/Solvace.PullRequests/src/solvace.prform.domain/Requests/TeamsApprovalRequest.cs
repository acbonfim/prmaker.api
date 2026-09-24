namespace solvace.prform.domain.Requests;

/// <summary>Pede aprovação de um PR do card no grupo do Teams (POST /Teams/approval).</summary>
public class TeamsApprovalRequest
{
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Id do registro em PullRequestsGithub (o mesmo id da lista de PRs do card).</summary>
    public int PullRequestGithubId { get; set; }
}
