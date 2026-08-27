namespace solvace.github.domain.Options;

public class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string Token { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string? Branch { get; set; }
}