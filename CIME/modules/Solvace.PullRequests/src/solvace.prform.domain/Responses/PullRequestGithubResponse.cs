namespace solvace.prform.domain.Responses;

public class PullRequestGithubResponse
{
    public int Id { get; set; }
    public int PullRequestRegisterId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string BranchPrefix { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>OPEN, MERGED ou CLOSED.</summary>
    public string Status { get; set; } = string.Empty;
    public bool IsDraft { get; set; }
    public DateTimeOffset? StatusSyncedAt { get; set; }

    /// <summary>true quando o GitHub não pôde ser consultado e o status devolvido é o último persistido.</summary>
    public bool StatusStale { get; set; }

    /// <summary>true quando já existia PR aberto para head→base e ele foi apenas registrado.</summary>
    public bool AlreadyExisted { get; set; }

    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
