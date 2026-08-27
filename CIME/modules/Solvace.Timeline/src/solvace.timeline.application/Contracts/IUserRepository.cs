namespace solvace.timeline.application.Contracts;

/// <summary>
/// Contrato para resolver o nome do usuário logado a partir do seu Id.
/// Implementado no host (que tem acesso à base de autenticação).
/// </summary>
public interface IUserRepository
{
    Task<string?> GetFullNameAsync(Guid userId, CancellationToken cancellationToken);
}
