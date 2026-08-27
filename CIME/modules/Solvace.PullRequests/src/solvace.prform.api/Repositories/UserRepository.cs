using Microsoft.EntityFrameworkCore;
using solvace.prform.Infra.Contexts;
using solvace.vacations.application.Contracts;

namespace solvace.prform.Repositories;

public class UserRepository(AuthenticationContext context) : IUserRepository
{
    public async Task<string?> GetFullNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user?.FullName;
    }

    public async Task<Dictionary<Guid, string>> GetFullNamesAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.ToList();
        var users = await context.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(u => u.Id, u => u.FullName);
    }

    public async Task<List<string>> GetDepartmentsAsync(CancellationToken cancellationToken)
    {
        return await context.Users
            .Where(u => u.Department != null && u.Department != string.Empty)
            .Select(u => u.Department!)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetUserIdsByDepartmentAsync(string department, CancellationToken cancellationToken)
    {
        return await context.Users
            .Where(u => u.Department == department)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
    }
}
