namespace solvace.prform.application.Contracts;

public interface ICommitable
{
    Task<bool> CommitAsync(CancellationToken cancellationToken);
}