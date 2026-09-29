namespace solvace.azure.domain.Requests;

/// <summary>
/// Classificação do card no DevOps (Resolution Type · General Classification · Classification) — feature 0028.
/// Informe <see cref="Preset"/> (uma das opções de GET Azure/actions/classifications) ou os três valores.
/// </summary>
public class ClassifyCardRequest
{
    public string? Preset { get; set; }
    public string? ResolutionType { get; set; }
    public string? GeneralClassification { get; set; }
    public string? Classification { get; set; }
}
