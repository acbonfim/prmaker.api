namespace solvace.azure.domain.Requests;

/// <summary>Resumo não técnico a publicar na discussion do card (feature 0011).</summary>
public class SaveSummaryRequest
{
    /// <summary>Texto em Markdown (gravado no PRMake).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>O mesmo texto em HTML (a discussion do DevOps renderiza HTML).</summary>
    public string Html { get; set; } = string.Empty;
}
