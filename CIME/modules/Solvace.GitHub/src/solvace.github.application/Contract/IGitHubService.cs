using solvace.github.domain.ValueObjects;
using solvace.github.domain.Responses;

namespace solvace.github.application.Contract;

public interface IGitHubService
{
    Task<HttpJsonResponse> CreatePullRequestAsync(string sourceBranch, string targetBranch, string title, bool draft, string? descriptionRaw, CancellationToken cancellationToken = default);
    Task<HttpJsonResponse> GetCardReferencesAsync(string cardNumber, int maxPerType, CancellationToken cancellationToken = default);
    Task<CommitDiffResponse> GetCommitDiffAsync(string sha, CancellationToken cancellationToken = default);
    Task<CompareDiffResponse> CompareRefsDiffAsync(string @base, string head, CancellationToken cancellationToken = default);
}