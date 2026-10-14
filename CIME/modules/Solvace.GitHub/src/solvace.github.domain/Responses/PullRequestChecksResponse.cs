namespace solvace.github.domain.Responses;

/// <summary>
/// CI de um PR aberto (0069): check runs (GitHub Actions) + statuses do commit de cabeça, resumidos num estado.
/// Error preenchido quando não foi possível consultar.
/// </summary>
public class PullRequestChecksResponse
{
    public const string Pending = "pending";
    public const string Success = "success";
    public const string Failure = "failure";
    /// <summary>O commit não tem nenhum check/status.</summary>
    public const string None = "none";

    public string Repository { get; set; } = string.Empty;
    public int Number { get; set; }
    public string? HeadSha { get; set; }
    public string State { get; set; } = None;
    /// <summary>
    /// Checks que falharam (nome e link para o log). <see cref="State"/> só é failure quando algum NÃO é
    /// <see cref="PullRequestCheckItem.Preexisting"/> — falha que a base já tem não é do PR.
    /// </summary>
    public List<PullRequestCheckItem> Failed { get; set; } = [];
    public string? Error { get; set; }
}

public class PullRequestCheckItem
{
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    /// <summary>O mesmo check também falha nos PRs recentes para a mesma branch: falha que já existe na base.</summary>
    public bool Preexisting { get; set; }
}
