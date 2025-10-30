using solvace.github.domain.ValueObjects;

namespace solvace.github.application.Contract;

public interface IGitHubService
{
    Task<HttpJsonResponse> CreatePullRequestAsync(string sourceBranch, string targetBranch, string title, bool draft, string? descriptionRaw, CancellationToken cancellationToken = default);
    Task<HttpJsonResponse> GetCardReferencesAsync(string cardNumber, int maxPerType, CancellationToken cancellationToken = default);
}