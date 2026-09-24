namespace solvace.prform.domain.Requests;

/// <summary>Atualiza título/descrição de um PR já aberto (PUT /PullRequest/{cardNumber}/github/{id}).</summary>
public class UpdatePullRequestGithubRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
}
