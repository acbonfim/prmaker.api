namespace solvace.executionplans.application.Contracts;

/// <summary>
/// 0106: um PR do plano passou a ter "changes requested" — implementado no host: cria o item "Atender o review do PR"
/// (pendente, sem duplicar), avisa na conversa do caso e registra o sinal de erro. Best-effort.
/// </summary>
public interface IPullRequestReviewHandler
{
    Task ChangesRequestedAsync(PullRequestChangesRequested request, CancellationToken ct);
}

/// <param name="OwnerId">Dono do plano (o item nasce dele; executa quem clicar).</param>
public record PullRequestChangesRequested(string Card, Guid PlanId, Guid? OwnerId, string Repository, int Number, string Url, string? Target,
    string? HeadSha, int Count);
