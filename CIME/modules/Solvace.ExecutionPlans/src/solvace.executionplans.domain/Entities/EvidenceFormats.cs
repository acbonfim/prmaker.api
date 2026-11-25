namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0076: formatos de evidência que a análise consegue ler (e por isso precisa abrir antes de concluir): imagem, PDF,
/// texto/log/dados, e-mail e documentos do Office (a skill converte para texto). Vídeo, áudio e compactados não travam —
/// ficam como "abrir à mão".
/// </summary>
public static class EvidenceFormats
{
    private static readonly HashSet<string> ReadableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".pdf", ".txt", ".log", ".csv", ".tsv", ".json", ".xml", ".html",
        ".htm", ".md", ".sql", ".eml", ".msg", ".docx", ".xlsx", ".pptx", ".har", ".yaml", ".yml", ".ini", ".config"
    };

    public static bool IsReadable(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName);
        if (ReadableExtensions.Contains(ext)) return true;
        var type = contentType ?? "";
        if (type.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return false;
        return type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !type.Contains("svg", StringComparison.OrdinalIgnoreCase)
               || type.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
               || type.Contains("pdf", StringComparison.OrdinalIgnoreCase)
               || type.Contains("json", StringComparison.OrdinalIgnoreCase);
    }
}
