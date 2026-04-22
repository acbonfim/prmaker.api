namespace solvace.vacations.application.Contracts;

public interface IUserRepository
{
    Task<string?> GetFullNameAsync(Guid userId, CancellationToken cancellationToken);
    Task<Dictionary<Guid, string>> GetFullNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);
}