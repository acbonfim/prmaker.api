using Microsoft.EntityFrameworkCore;
using solvace.prform.Infra.Contexts;
using solvace.timeline.application.Contracts;

namespace solvace.prform.Repositories;

/// <summary>
/// Implementação, no host, do contrato de resolução de nome de usuário do módulo Timeline.
/// Consulta a base de autenticação (AspNetUsers).
/// </summary>
public class TimelineUserRepository(AuthenticationContext context) : IUserRepository
{
    public async Task<string?> GetFullNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user?.FullName;
    }
}
