namespace solvace.github.domain.Responses;

/// <summary>0085 r3: branch do repositório para o autocomplete do editor de itens de trabalho.</summary>
public class BranchInfoResponse
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Branch protegida no GitHub.</summary>
    public bool Protected { get; set; }

    /// <summary>Branch padrão do repositório.</summary>
    public bool IsDefault { get; set; }
}
