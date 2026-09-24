using solvace.github.domain.Responses;

namespace solvace.github.application.Contract;

public interface IGitHubService
{
    /// <param name="repository">Repositório do owner configurado; quando nulo usa o "Repo" do plugin.</param>
    Task<PullRequestResponse?> CreatePullRequestAsync(string sourceBranch, string targetBranch, string title, bool draft, string? descriptionRaw, CancellationToken cancellationToken = default, string? repository = null);
    Task<PullRequestResponse?> UpdatePullRequestAsync(string repository, int number, string title, string? descriptionRaw, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepositoryResponse>> ListRepositoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PullRequestStatusResponse>> GetPullRequestsStatusAsync(IEnumerable<(string Repository, int Number)> pullRequests, CancellationToken cancellationToken = default);
    Task<CardReferencesResponse?> GetCardReferencesAsync(string cardNumber, int maxPerType, CancellationToken cancellationToken = default);
    Task<CommitDiffResponse?> GetCommitDiffAsync(string sha, CancellationToken cancellationToken = default, string? repository = null);
    Task<CompareDiffResponse?> CompareRefsDiffAsync(string @base, string head, CancellationToken cancellationToken = default);
    Task<List<BranchCommitResponse>> GetBranchCommitsAsync(string repository, string branch, CancellationToken cancellationToken = default);
}
