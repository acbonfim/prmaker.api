using System.Text;
using System.Text.RegularExpressions;
using solvace.knowledge.domain.Entities;

namespace solvace.knowledge.application;

/// <summary>Anexo como chega da tela (0072): nome, tipo informado e o conteúdo em base64.</summary>
public sealed record AskAttachmentInput(string? Name, string? MediaType, string? Data);

/// <summary>Anexo validado: tipo conferido pelo conteúdo, bytes já decodificados (texto longo cortado).</summary>
public sealed record AskAttachment(string Name, string MediaType, byte[] Data, string Kind)
{
    public bool IsImage => Kind == AskAttachmentRules.KindImage;
    public bool IsPdf => Kind == AskAttachmentRules.KindPdf;
    public bool IsText => Kind == AskAttachmentRules.KindText;
}

public sealed record AskAttachmentsChecked(List<AskAttachment> Attachments, List<string> Notes);

/// <summary>
/// Regras dos anexos do Pergunte (0072): quantidade, tipo (conferido pelo conteúdo, não só pelo que a tela diz), tamanho, PDF com
/// poucas páginas e texto sem binário. Anexo é processado e descartado — nunca gravado. A tela mostra o motivo de cada recusa
/// (<see cref="DomainException"/>).
/// </summary>
public static partial class AskAttachmentRules
{
    public const string KindImage = "image";
    public const string KindPdf = "pdf";
    public const string KindText = "text";

    public const int MaxFiles = 4;
    public const int MaxImageBytes = 5 * 1024 * 1024;
    public const int MaxPdfBytes = 5 * 1024 * 1024;
    public const int MaxPdfPages = 10;
    public const int MaxTextBytes = 100 * 1024;
    public const int MaxTotalBytes = 12 * 1024 * 1024;

    /// <summary>Extensões de texto aceitas quando o navegador não informa o tipo (logs vêm como vazio ou octet-stream).</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".csv", ".tsv", ".json", ".md", ".xml", ".yml", ".yaml", ".sql", ".sh", ".config", ".ini", ".env", ".html", ".htm", ".js", ".ts", ".cs", ".cshtml", ".asp", ".aspx"
    };

    public static AskAttachmentsChecked Validate(IReadOnlyList<AskAttachmentInput>? inputs)
    {
        var result = new AskAttachmentsChecked([], []);
        if (inputs is not { Count: > 0 }) return result;
        if (inputs.Count > MaxFiles) throw new DomainException($"Anexe no máximo {MaxFiles} arquivos por pergunta.");

        long total = 0;
        foreach (var input in inputs)
        {
            var name = CleanName(input.Name);
            byte[] data;
            try { data = Convert.FromBase64String(StripDataUrl(input.Data)); }
            catch (FormatException) { throw new DomainException($"O anexo \"{name}\" veio corrompido. Anexe de novo."); }
            if (data.Length == 0) throw new DomainException($"O anexo \"{name}\" está vazio.");
            total += data.Length;
            if (total > MaxTotalBytes) throw new DomainException($"Os anexos passam de {MaxTotalBytes / 1024 / 1024} MB no total. Anexe menos arquivos ou arquivos menores.");

            var sniffed = Sniff(data);
            var declared = (input.MediaType ?? string.Empty).Trim().ToLowerInvariant();
            if (sniffed is { } known)
            {
                if (known.Kind == KindImage && data.Length > MaxImageBytes)
                    throw new DomainException($"A imagem \"{name}\" passa de {MaxImageBytes / 1024 / 1024} MB. Reduza o tamanho ou recorte só a parte que importa.");
                if (known.Kind == KindPdf)
                {
                    if (data.Length > MaxPdfBytes) throw new DomainException($"O PDF \"{name}\" passa de {MaxPdfBytes / 1024 / 1024} MB.");
                    var pages = CountPdfPages(data);
                    if (pages > MaxPdfPages) throw new DomainException($"O PDF \"{name}\" tem {pages} páginas; o limite é {MaxPdfPages}. Anexe só as páginas que importam.");
                }
                result.Attachments.Add(new AskAttachment(name, known.MediaType, data, known.Kind));
                continue;
            }

            // não é imagem nem PDF pelo conteúdo: só texto — e o tipo ou a extensão têm que ser de texto
            if (!IsTextType(declared, name))
                throw new DomainException($"O tipo do anexo \"{name}\" não é aceito. Use imagem (PNG, JPEG, WebP, GIF), PDF ou arquivo de texto (log, CSV, JSON, MD…).");
            if (Array.IndexOf(data, (byte)0, 0, Math.Min(data.Length, 8192)) >= 0)
                throw new DomainException($"O anexo \"{name}\" parece binário, não texto.");
            if (data.Length > MaxTextBytes)
            {
                data = CutUtf8(data, MaxTextBytes);
                result.Notes.Add($"O arquivo \"{name}\" é grande: só os primeiros {MaxTextBytes / 1024} KB foram lidos.");
            }
            result.Attachments.Add(new AskAttachment(name, "text/plain", data, KindText));
        }
        return result;
    }

    /// <summary>Tipo pelo cabeçalho do arquivo (PNG, JPEG, GIF, WebP, PDF); null = não é um deles.</summary>
    public static (string MediaType, string Kind)? Sniff(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return ("image/png", KindImage);
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return ("image/jpeg", KindImage);
        if (d.Length >= 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F' && d[3] == '8') return ("image/gif", KindImage);
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P') return ("image/webp", KindImage);
        if (d.Length >= 5 && d[0] == '%' && d[1] == 'P' && d[2] == 'D' && d[3] == 'F' && d[4] == '-') return ("application/pdf", KindPdf);
        return null;
    }

    /// <summary>
    /// Páginas do PDF contando os objetos <c>/Type /Page</c>. Aproximação: PDF com os objetos dentro de fluxos comprimidos
    /// (1.5+) pode contar menos e passar do limite — a API da IA ainda recusa o que for grande demais.
    /// </summary>
    public static int CountPdfPages(byte[] data) => PdfPage().Matches(Encoding.Latin1.GetString(data)).Count;

    private static bool IsTextType(string declared, string name)
    {
        if (declared.StartsWith("text/", StringComparison.Ordinal)) return true;
        if (declared is "application/json" or "application/xml" or "application/x-ndjson" or "application/csv" or "application/yaml" or "application/x-yaml" or "application/sql" or "application/x-sh") return true;
        return TextExtensions.Contains(Path.GetExtension(name));
    }

    private static string CleanName(string? name)
    {
        var clean = Path.GetFileName((name ?? string.Empty).Trim().Replace('\\', '/'));
        clean = new string(clean.Where(c => !char.IsControl(c)).ToArray());
        if (clean.Length == 0) clean = "anexo";
        return clean.Length <= 80 ? clean : clean[..80];
    }

    private static string StripDataUrl(string? data)
    {
        var value = (data ?? string.Empty).Trim();
        var comma = value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? value.IndexOf(',') : -1;
        return comma >= 0 ? value[(comma + 1)..] : value;
    }

    /// <summary>Corta no limite sem partir um caractere UTF-8 ao meio.</summary>
    private static byte[] CutUtf8(byte[] data, int max)
    {
        var end = max;
        while (end > 0 && (data[end] & 0b1100_0000) == 0b1000_0000) end--;
        return data[..end];
    }

    [GeneratedRegex(@"/Type\s*/Page(?![A-Za-z])")]
    private static partial Regex PdfPage();
}
