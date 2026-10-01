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

/// <summary>
/// "Salvar o card" (0037): garante o registro do card no PRMake (PullRequest) — o mesmo do botão Salvar da tela — assim
/// que a skill cria o plano, sem esperar o fim (gerar-prmake). Implementado no host sobre o módulo de PRs.
/// </summary>
public interface IExecutionCardRegistrar
{
    /// <returns>true quando criou o registro agora; false se ele já existia (nada muda).</returns>
    Task<bool> EnsureRegisteredAsync(string cardNumber, Guid userId, CancellationToken cancellationToken);
}

/// <summary>Escreve na Timeline do card (0024) — implementado no host sobre o módulo Timeline.</summary>
public interface IExecutionTimelineWriter
{
    Task WriteAsync(string cardNumber, string markdown, Guid? userId, string actorName, CancellationToken cancellationToken);
}
