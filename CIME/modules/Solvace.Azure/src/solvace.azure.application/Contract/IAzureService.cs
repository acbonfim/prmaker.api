using System.Threading;
using System.Threading.Tasks;
using solvace.azure.domain.Models;

namespace solvace.azure.application.Contract;

public interface IAzureService
{
    Task<AzureWorkItem?> GetCardAsync(string id, CancellationToken cancellationToken = default);
    Task<AzureWorkItem?> UpdateRootCauseAsync(string id, string bodyRaw, CancellationToken cancellationToken = default);
}