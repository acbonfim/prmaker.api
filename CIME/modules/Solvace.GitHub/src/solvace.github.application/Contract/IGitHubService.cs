using solvace.github.domain.Responses;

namespace solvace.github.application.Contract;

public interface IGitHubService
{
    /// <param name="repository">Repositório do owner configurado; quando nulo usa o "Repo" do plugin.</param>
    Task<PullRequestResponse?> CreatePullRequestAsync(string sourceBranch, string targetBranch, string title, bool draft, string? descriptionRaw, CancellationToken cancellationToken = default, string? repository = null);
    Task<PullRequestResponse?> UpdatePullRequestAsync(string repository, int number, string title, string? descriptionRaw, CancellationToken cancellationToken = default);
    /// <param name="targetStatus">OPEN, DRAFT ou CLOSED (ver PullRequestStatusTarget).</param>
    Task<PullRequestResponse?> SetPullRequestStatusAsync(string repository, int number, string targetStatus, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepositoryResponse>> ListRepositoriesAsync(CancellationToken cancellationToken = default);
    /// <param name="bypassCache">true ignora o cache de status (ex.: botão "atualizar" da tela).</param>
    Task<IReadOnlyList<PullRequestStatusResponse>> GetPullRequestsStatusAsync(IEnumerable<(string Repository, int Number)> pullRequests, CancellationToken cancellationToken = default, bool bypassCache = false);
    /// <summary>0069: CI do PR (check runs + statuses do commit de cabeça), com cache de 1 min.</summary>
    Task<PullRequestChecksResponse> GetPullRequestChecksAsync(string repository, int number, CancellationToken cancellationToken = default);
    Task<CardReferencesResponse?> GetCardReferencesAsync(string cardNumber, int maxPerType, CancellationToken cancellationToken = default);
    Task<CommitDiffResponse?> GetCommitDiffAsync(string sha, CancellationToken cancellationToken = default, string? repository = null);
    Task<CompareDiffResponse?> CompareRefsDiffAsync(string @base, string head, CancellationToken cancellationToken = default);
    Task<List<BranchCommitResponse>> GetBranchCommitsAsync(string repository, string branch, CancellationToken cancellationToken = default);

    /// <summary>0074: nomes de todas as branches do repositório (cache curto). Quem chama filtra.</summary>
    /// <param name="bypassCache">true consulta o GitHub de novo (branch recém-criada).</param>
    Task<IReadOnlyList<string>> ListBranchesAsync(string repository, CancellationToken cancellationToken = default, bool bypassCache = false);
    /// <summary>0074: a branch existe no remoto?</summary>
    Task<bool> BranchExistsAsync(string repository, string branch, CancellationToken cancellationToken = default);
    /// <summary>0074: commits de <paramref name="head"/> que não estão em <paramref name="base"/> (sem merges); null se alguma das refs não existe.</summary>
    Task<BranchDeltaResponse?> GetBranchDeltaAsync(string repository, string @base, string head, CancellationToken cancellationToken = default);
}
