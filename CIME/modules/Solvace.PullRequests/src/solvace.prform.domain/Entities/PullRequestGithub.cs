using solvace.prform.domain.Enums;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.domain.Entities;

/// <summary>
/// Pull Request aberto no GitHub pelo CIME para um card. Um card (<see cref="PullRequestRegister"/>)
/// pode ter vários, um por repositório/branch. Guarda um snapshot do título e da descrição enviados.
/// </summary>
public class PullRequestGithub : IEntity<int>, IAuditableEntity
{
    public const int MaxRepositoryIdLength = 200;
    public const int MaxBranchPrefixLength = 50;
    public const int MaxBranchNameLength = 200;
    public const int MaxUrlLength = 500;
    public const int MaxTitleLength = 500;
    public const int MaxStatusLength = 20;

    public int Id { get; set; }

    public int PullRequestRegisterId { get; private set; }
    public PullRequestRegister? PullRequestRegister { get; private set; }

    public string CardNumber { get; private set; } = string.Empty;
    public string RepositoryId { get; private set; } = string.Empty;
    public string BranchPrefix { get; private set; } = string.Empty;
    public string BranchName { get; private set; } = string.Empty;
    public string TargetBranch { get; private set; } = string.Empty;

    public int GithubPrNumber { get; private set; }
    public long GithubPrId { get; private set; }
    public string Url { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public string Status { get; private set; } = PullRequestGithubStatus.Open;
    public bool IsDraft { get; private set; }
    public DateTimeOffset? StatusSyncedAt { get; private set; }

    /// <summary>Usuário do CIME (externalId) que abriu o PR. No GitHub o autor é sempre a conta do token.</summary>
    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    protected PullRequestGithub() { }

    public PullRequestGithub(PullRequestRegister register, OpenPullRequestGithubRequest request,
        int githubPrNumber, long githubPrId, string url, string status, bool isDraft)
    {
        PullRequestRegister = register ?? throw new DomainException("Pull request register is required");
        PullRequestRegisterId = register.Id;
        CardNumber = register.CardNumber;

        RepositoryId = Required(request.RepositoryId, nameof(RepositoryId), MaxRepositoryIdLength);
        BranchPrefix = Optional(request.BranchPrefix, nameof(BranchPrefix), MaxBranchPrefixLength);
        BranchName = Required(request.BranchName, nameof(BranchName), MaxBranchNameLength);
        TargetBranch = Required(request.TargetBranch, nameof(TargetBranch), MaxBranchNameLength);
        UserId = request.UserId;

        if (githubPrNumber <= 0)
            throw new DomainException("GitHub pull request number is required");

        GithubPrNumber = githubPrNumber;
        GithubPrId = githubPrId;
        Url = Required(url, nameof(Url), MaxUrlLength);

        SetContent(request.Title, request.Description);
        SetStatus(status, isDraft);
        CreatedAt = DateTime.UtcNow;
    }

    public void SetContent(string title, string? description)
    {
        Title = Required(title, nameof(Title), MaxTitleLength);
        Description = description ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetStatus(string status, bool isDraft)
    {
        if (status is not (PullRequestGithubStatus.Open or PullRequestGithubStatus.Merged or PullRequestGithubStatus.Closed))
            throw new DomainException($"Invalid pull request status '{status}'");

        Status = status;
        IsDraft = isDraft;
        StatusSyncedAt = DateTimeOffset.UtcNow;
    }

    public PullRequestGithubResponse ToResponse() =>
        new()
        {
            Id = Id,
            PullRequestRegisterId = PullRequestRegisterId,
            CardNumber = CardNumber,
            RepositoryId = RepositoryId,
            BranchPrefix = BranchPrefix,
            BranchName = BranchName,
            TargetBranch = TargetBranch,
            Number = GithubPrNumber,
            Url = Url,
            Title = Title,
            Description = Description,
            Status = Status,
            IsDraft = IsDraft,
            StatusSyncedAt = StatusSyncedAt,
            UserId = UserId,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };

    private static string Required(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{field} cannot be empty");

        return Optional(value, field, maxLength);
    }

    private static string Optional(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength} characters");

        return trimmed;
    }
}
