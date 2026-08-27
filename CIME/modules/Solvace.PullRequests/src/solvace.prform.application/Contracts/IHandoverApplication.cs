using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.application.Contracts;

public interface IHandoverApplication : ICommitable
{
    Task<HandoverResponse?> GetByCardNumber(string cardNumber, CancellationToken cancellationToken);
    Task<HandoverResponse> Save(HandoverRequest request, CancellationToken cancellationToken);
}
