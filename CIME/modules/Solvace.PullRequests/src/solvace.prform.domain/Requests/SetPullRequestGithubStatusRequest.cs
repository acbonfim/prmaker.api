namespace solvace.prform.domain.Requests;

/// <summary>Troca o status de um PR no GitHub (PUT /PullRequest/{cardNumber}/github/{id}/status).</summary>
public class SetPullRequestGithubStatusRequest
{
    /// <summary>OPEN (aberto/pronto para revisão), DRAFT ou CLOSED.</summary>
    public string Status { get; set; } = string.Empty;
}
