namespace solvace.github.domain.Responses;

/// <summary>Repositório do owner configurado no plugin, para seleção no front.</summary>
public class RepositoryResponse
{
    /// <summary>Nome do repositório (usado como RepositoryId no CIME).</summary>
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Private { get; set; }
    public string DefaultBranch { get; set; } = string.Empty;
}
