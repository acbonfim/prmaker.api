using System.Threading;
using System.Threading.Tasks;
using solvace.azure.domain.ValueObjects;

namespace solvace.azure.application.Contract;

public interface IAzureService
{
    Task<HttpJsonResponse> GetCardAsync(string id, CancellationToken cancellationToken = default);
    Task<HttpJsonResponse> UpdateRootCauseAsync(string id, string bodyRaw, CancellationToken cancellationToken = default);
}