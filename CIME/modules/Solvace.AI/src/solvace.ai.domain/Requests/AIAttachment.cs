namespace solvace.ai.domain.Requests;

/// <summary>
/// Anexo de uma chamada de IA (0072): imagem (PNG/JPEG/WebP/GIF), PDF ou texto (log, CSV, JSON, MD…). Vai só na chamada — nunca é
/// gravado. Quem valida tipo, tamanho e quantidade é quem recebe o anexo (a API); o provedor só o converte para o formato dele.
/// </summary>
public sealed record AIAttachment(string Name, string MediaType, byte[] Data)
{
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public bool IsPdf => MediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    public bool IsText => !IsImage && !IsPdf;
}
