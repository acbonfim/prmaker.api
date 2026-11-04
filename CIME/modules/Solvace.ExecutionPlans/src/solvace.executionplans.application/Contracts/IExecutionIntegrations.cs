using solvace.executionplans.domain.Entities;

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

    /// <summary>0069: CI do PR aberto (cache de 1 min). null = não deu para consultar (fica o último lido).</summary>
    Task<CardPullRequestChecks?> GetChecksAsync(string repository, int number, CancellationToken cancellationToken);
}

/// <summary>0069: CI de um PR — State pending | success | failure; null = o commit não tem CI. HeadSha = commit avaliado.</summary>
public record CardPullRequestChecks(string? State, string? HeadSha, IReadOnlyList<ExecutionCheckItem> Failed)
{
    /// <summary>0075: revisões do PR (aprovações vigentes, pedidos de alteração vigentes, revisores ainda pedidos).</summary>
    public int Approvals { get; init; }
    public int ChangesRequested { get; init; }
    public IReadOnlyList<string> ReviewersPending { get; init; } = [];
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

/// <summary>
/// 0075: o card no DevOps para o plano — branch de QA e automação (Skills Configurations), mover para Dev Test in QA,
/// estado/coluna atuais e o comentário da situação. Implementado no host com a integração de quem está chamando.
/// </summary>
public interface IExecutionCardDevOps
{
    Task<ExecutionQaSettings> GetQaSettingsAsync(CancellationToken cancellationToken);

    /// <returns>null = movido; senão o motivo (sem integração, 409 do DevOps...).</returns>
    Task<string?> MoveToDevTestInQaAsync(string cardNumber, CancellationToken cancellationToken);

    /// <summary>Estado e coluna do board (null quando não deu para ler — é enfeite do resumo).</summary>
    Task<(string? State, string? Column)> GetStateAsync(string cardNumber, CancellationToken cancellationToken);

    /// <summary>Cria ou atualiza o comentário HTML na discussion; devolve o id.</summary>
    Task<int> UpsertCommentAsync(string cardNumber, string html, int? commentId, CancellationToken cancellationToken);
}

/// <summary>
/// 0075: <c>AutoDevTestInQa</c>, as branches de QA (<c>QaBranchPattern</c>: lista; <c>qa*</c> = prefixo) e
/// <c>SituationPostToDevOps</c> (o resumo da situação vai sozinho para a discussion do card).
/// </summary>
public record ExecutionQaSettings(bool AutoDevTestInQa, IReadOnlyList<string> QaBranches, bool PostSituationToDevOps = true)
{
    public static readonly ExecutionQaSettings Default = new(true, ["qa"]);

    public bool IsQaBranch(string? branch)
    {
        var b = branch?.Trim();
        if (string.IsNullOrEmpty(b)) return false;
        return QaBranches.Any(p => p.EndsWith('*')
            ? b.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(b, p, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>0076: evidência do card no DevOps (anexo, imagem colada num campo ou na discussion).</summary>
public record CardEvidence(string Key, string Url, string FileName, string Source, string? SourceDetail, DateTimeOffset? At, long? Size, string? Comment);

/// <summary>0076: conteúdo baixado (TooLarge = passou do limite, sem conteúdo).</summary>
public record CardEvidenceFile(byte[] Data, string ContentType, bool TooLarge, long? Size);

/// <summary>0076: evidências do card no DevOps — implementado no host com a integração do DevOps de quem chama.</summary>
public interface IExecutionCardEvidenceSource
{
    Task<IReadOnlyList<CardEvidence>> ListAsync(string cardNumber, CancellationToken cancellationToken);
    Task<CardEvidenceFile> DownloadAsync(CardEvidence evidence, long maxBytes, CancellationToken cancellationToken);
}

/// <summary>Escreve na Timeline do card (0024) — implementado no host sobre o módulo Timeline.</summary>
public interface IExecutionTimelineWriter
{
    Task WriteAsync(string cardNumber, string markdown, Guid? userId, string actorName, CancellationToken cancellationToken);
}
