namespace solvace.azure.domain.Requests;

/// <summary>Resumo não técnico a publicar na discussion do card (feature 0011).</summary>
public class SaveSummaryRequest
{
    /// <summary>Texto em Markdown (gravado no PRMake).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>O mesmo texto em HTML (a discussion do DevOps renderiza HTML). Obrigatório para publicar.</summary>
    public string? Html { get; set; }

    /// <summary>
    /// true (padrão, usado pela skill) = publica na discussion e grava; false = só grava no PRMake.
    /// </summary>
    public bool Publish { get; set; } = true;
}
