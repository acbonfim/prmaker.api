using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.application.Contracts;

public interface IPullRequestApplication:ICommitable
{
    Task<PullRequestRegisterResponse> Create(PullRequestRegisterRequest request,CancellationToken cancellationToken);
    Task<PullRequestRegisterResponse> Get(int id, CancellationToken cancellationToken);
    Task<PullRequestRegisterResponse?> GetByCardNumber(string cardNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<PullRequestRecentResponse>> GetRecentByUser(Guid userId, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Grava o resumo não técnico e, quando publicado na discussion, o id do comentário; avisa em
    /// tempo real. Sem registro do card lança DomainException.
    /// </summary>
    Task<PullRequestRegisterResponse> SaveSummary(string cardNumber, string summary, int? publishedCommentId, CancellationToken cancellationToken);
}
