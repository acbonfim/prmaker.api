using solvace.github.domain.Entities;

namespace solvace.github.infra.Repositories;

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


