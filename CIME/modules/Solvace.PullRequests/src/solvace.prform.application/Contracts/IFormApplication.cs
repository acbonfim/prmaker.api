using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.application.Contracts;

public interface IFormApplication : ICommitable
{
    Task<FormResponse> Create(FormRequest request,CancellationToken cancellationToken);
    Task<FormResponse> Get(int id, CancellationToken cancellationToken);
    Task<FormResponse> GetByEnvironment(string enrironmentName, CancellationToken cancellationToken);
    Task<IEnumerable<FormResponse>> GetForms(CancellationToken cancellationToken);
}