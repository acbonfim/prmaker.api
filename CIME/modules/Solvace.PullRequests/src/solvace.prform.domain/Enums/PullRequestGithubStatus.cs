namespace solvace.prform.domain.Enums;

/// <summary>
/// Status de um PR no GitHub, persistido como texto. MERGED/CLOSED são terminais
/// (não precisam ser consultados novamente no GitHub). LEGACY marca os registros migrados
/// do modelo antigo (um PullRequest por card x repositório): têm branch/repositório, mas não
/// um PR no GitHub — também não são consultados.
/// </summary>
public static class PullRequestGithubStatus
{
    public const string Open = "OPEN";
    public const string Merged = "MERGED";
    public const string Closed = "CLOSED";
    public const string Legacy = "LEGACY";

    public static bool IsTerminal(string? status) => status is Merged or Closed or Legacy;

    /// <summary>Converte o estado da API do GitHub (open/closed + merged) no status do CIME.</summary>
    public static string From(string? githubState, bool merged)
    {
        if (merged) return Merged;
        return string.Equals(githubState, "closed", StringComparison.OrdinalIgnoreCase) ? Closed : Open;
    }
}
