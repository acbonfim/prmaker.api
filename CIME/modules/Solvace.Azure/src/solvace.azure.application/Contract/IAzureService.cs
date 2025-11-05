using System.Threading;
using System.Threading.Tasks;
using solvace.azure.domain.Models;
using solvace.azure.domain.Requests;

namespace solvace.azure.application.Contract;

public interface IAzureService
{
    Task<AzureWorkItem?> GetCardAsync(string id, CancellationToken cancellationToken = default);
    Task<AzureWorkItem?> UpdateRootCauseAsync(string id, UpdateRootCauseRequest bodyRaw, CancellationToken cancellationToken = default);
}