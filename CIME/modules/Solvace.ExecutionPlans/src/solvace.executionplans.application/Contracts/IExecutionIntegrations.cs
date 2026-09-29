namespace solvace.executionplans.application.Contracts;

/// <summary>PR do GitHub registrado no card (0024) — status normalizado: open | merged | closed.</summary>
public record CardPullRequest(string Repository, int? Number, string Url, string BaseBranch, string HeadBranch, string Status, string Title, DateTimeOffset CreatedAt);

/// <summary>
/// PRs do card com o status atual do GitHub (0024). Implementado no host sobre o módulo GitHub
/// (mesma sincronização da tela, com cache de 60 s e a credencial de quem está consultando).
/// </summary>
public interface IExecutionPullRequestSource
{
    Task<IReadOnlyList<CardPullRequest>> ListByCardAsync(string cardNumber, CancellationToken cancellationToken);
}

/// <summary>Escreve na Timeline do card (0024) — implementado no host sobre o módulo Timeline.</summary>
public interface IExecutionTimelineWriter
{
    Task WriteAsync(string cardNumber, string markdown, Guid? userId, string actorName, CancellationToken cancellationToken);
}
