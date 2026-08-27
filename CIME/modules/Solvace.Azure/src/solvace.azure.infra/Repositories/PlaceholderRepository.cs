using solvace.azure.domain.Entities;

namespace solvace.azure.infra.Repositories;

public interface IPlaceholderRepository
{
    Task<PlaceholderEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

public class PlaceholderRepository : IPlaceholderRepository
{
    public Task<PlaceholderEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PlaceholderEntity?>(null);
    }
}


