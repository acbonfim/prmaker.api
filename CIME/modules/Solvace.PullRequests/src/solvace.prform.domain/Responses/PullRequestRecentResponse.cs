namespace solvace.prform.domain.Responses;

/// <summary>
/// Item enxuto para listar os últimos Pull Requests registrados por um usuário
/// (usado no atalho "seus últimos cards" da home).
/// </summary>
public class PullRequestRecentResponse
{
    public int Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? RepositoryId { get; set; }
    public string BranchPrefix { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
