using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.application.Contracts;

public interface IPullRequestApplication:ICommitable
{
    Task<PullRequestRegisterResponse> Create(PullRequestRegisterRequest request,CancellationToken cancellationToken);
    Task<PullRequestRegisterResponse> Get(int id, CancellationToken cancellationToken);
    Task<PullRequestRegisterResponse> GetByCardNumber(string cardNumber, CancellationToken cancellationToken);
}