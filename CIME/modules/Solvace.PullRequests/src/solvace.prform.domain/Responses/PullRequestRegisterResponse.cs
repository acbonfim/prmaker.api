namespace solvace.prform.domain.Responses;

public class PullRequestRegisterResponse
{
    public int Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Resumo não técnico (Markdown) publicado na discussion do card (0011).</summary>
    public string? Summary { get; set; }

    /// <summary>Id do comentário do resumo na discussion do DevOps.</summary>
    public int? SummaryCommentId { get; set; }

    public DateTimeOffset? SummaryUpdatedAt { get; set; }

    /// <summary>Última publicação na discussion (anterior a SummaryUpdatedAt = alterações não publicadas).</summary>
    public DateTimeOffset? SummaryPublishedAt { get; set; }
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