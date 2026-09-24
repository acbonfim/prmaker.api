using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.github.application.Contract;

/// <summary>
/// PRs do GitHub de um card: abre/atualiza no GitHub e persiste em PullRequestsGithub.
/// Fica no módulo GitHub porque o prform.application não pode depender do IGitHubService
/// (o módulo GitHub já depende do prform para o cache de plugins).
/// Erros de validação/GitHub são lançados como DomainException (mensagem pronta para o usuário).
/// </summary>
public interface IPullRequestGithubApplication
{
    Task<PullRequestGithubResponse> Open(string cardNumber, OpenPullRequestGithubRequest request, CancellationToken cancellationToken);
    Task<PullRequestGithubResponse> Update(string cardNumber, int id, UpdatePullRequestGithubRequest request, CancellationToken cancellationToken);
    /// <summary>Troca o status no GitHub (OPEN, DRAFT ou CLOSED) e grava o novo status.</summary>
    Task<PullRequestGithubResponse> SetStatus(string cardNumber, int id, SetPullRequestGithubStatusRequest request, CancellationToken cancellationToken);
    /// <param name="forceRefresh">Com refreshStatus, ignora o cache de status do GitHub.</param>
    Task<IReadOnlyList<PullRequestGithubResponse>> ListByCard(string cardNumber, bool refreshStatus, CancellationToken cancellationToken, bool forceRefresh = false);
}
