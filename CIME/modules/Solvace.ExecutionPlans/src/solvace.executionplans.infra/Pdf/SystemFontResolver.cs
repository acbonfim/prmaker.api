using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace solvace.executionplans.infra.Pdf;

/// <summary>
/// 0099: fontes do PDF lidas das pastas do sistema (PDFsharp "core" não acha fonte sozinho fora do Windows). Famílias
/// lógicas: <see cref="Sans"/> (DejaVu Sans → Liberation Sans → Arial) e <see cref="Mono"/> (DejaVu Sans Mono → Liberation
/// Mono → Courier New). No container da API vem do pacote <c>fonts-dejavu-core</c> (Dockerfile); no macOS, Arial/Courier
/// New de <c>/System/Library/Fonts/Supplemental</c>. Sem nenhuma, cai na Segoe WP embutida no PDFsharp (só a sans).
/// <c>PDF_FONTS_DIR</c> (variável de ambiente) é procurada primeiro.
/// </summary>
public sealed class SystemFontResolver : IFontResolver
{
    public const string Sans = "PRMake Sans";
    public const string Mono = "PRMake Mono";

    /// <summary>Arquivos de uma família: regular, negrito, itálico, negrito+itálico (nomes alternativos separados por |).</summary>
    private sealed record Candidate(string Name, string Regular, string Bold, string Italic, string BoldItalic);

    private static readonly Candidate[] SansCandidates =
    [
        new("DejaVu Sans", "DejaVuSans.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf"),
        new("Liberation Sans", "LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf", "LiberationSans-Italic.ttf", "LiberationSans-BoldItalic.ttf"),
        new("Arial", "Arial.ttf|arial.ttf", "Arial Bold.ttf|arialbd.ttf", "Arial Italic.ttf|ariali.ttf", "Arial Bold Italic.ttf|arialbi.ttf"),
    ];

    private static readonly Candidate[] MonoCandidates =
    [
        new("DejaVu Sans Mono", "DejaVuSansMono.ttf", "DejaVuSansMono-Bold.ttf", "DejaVuSansMono-Oblique.ttf", "DejaVuSansMono-BoldOblique.ttf"),
        new("Liberation Mono", "LiberationMono-Regular.ttf", "LiberationMono-Bold.ttf", "LiberationMono-Italic.ttf", "LiberationMono-BoldItalic.ttf"),
        new("Courier New", "Courier New.ttf|cour.ttf", "Courier New Bold.ttf|courbd.ttf", "Courier New Italic.ttf|couri.ttf", "Courier New Bold Italic.ttf|courbi.ttf"),
    ];

    private const string EmbeddedRegular = "embedded:segoewp";
    private const string EmbeddedBold = "embedded:segoewp-bold";

    private readonly Lazy<Dictionary<string, string>> _index = new(BuildIndex);
    private readonly ConcurrentDictionary<string, byte[]> _data = new(StringComparer.Ordinal);

    /// <summary>Qual fonte real cada família lógica usa (para log/diagnóstico).</summary>
    public string Describe()
    {
        var sans = Pick(SansCandidates)?.Name ?? "Segoe WP (embutida)";
        var mono = Pick(MonoCandidates)?.Name ?? sans;
        return $"sans: {sans}; mono: {mono}";
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var mono = string.Equals(familyName, Mono, StringComparison.OrdinalIgnoreCase)
                   || familyName.Contains("mono", StringComparison.OrdinalIgnoreCase)
                   || familyName.Contains("courier", StringComparison.OrdinalIgnoreCase);
        var candidate = (mono ? Pick(MonoCandidates) : null) ?? Pick(SansCandidates);
        if (candidate is null)
            return new FontResolverInfo(bold ? EmbeddedBold : EmbeddedRegular, false, italic);

        var wanted = (bold, italic) switch
        {
            (true, true) => candidate.BoldItalic,
            (true, false) => candidate.Bold,
            (false, true) => candidate.Italic,
            _ => candidate.Regular
        };
        if (Find(wanted) is { } path)
            return new FontResolverInfo(path);
        // Sem a variante: a regular com negrito/itálico simulados.
        var regular = Find(candidate.Regular)!;
        return new FontResolverInfo(regular, bold, italic);
    }

    public byte[]? GetFont(string faceName) => _data.GetOrAdd(faceName, name => name switch
    {
        EmbeddedRegular => PdfSharp.WPFonts.FontDataHelper.SegoeWP,
        EmbeddedBold => PdfSharp.WPFonts.FontDataHelper.SegoeWPBold,
        _ => File.ReadAllBytes(name)
    });

    private Candidate? Pick(IEnumerable<Candidate> candidates) => candidates.FirstOrDefault(c => Find(c.Regular) is not null);

    private string? Find(string names)
    {
        foreach (var name in names.Split('|'))
            if (_index.Value.TryGetValue(name.ToLowerInvariant(), out var path))
                return path;
        return null;
    }

    /// <summary>Pastas de fontes do sistema (Linux, macOS, Windows) — nome do arquivo (minúsculo) → caminho.</summary>
    private static Dictionary<string, string> BuildIndex()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dirs = new List<string?>
        {
            Environment.GetEnvironmentVariable("PDF_FONTS_DIR"),
            "/usr/share/fonts", "/usr/local/share/fonts",
            string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".fonts"),
            string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".local", "share", "fonts"),
            "/System/Library/Fonts", "/Library/Fonts",
            string.IsNullOrEmpty(home) ? null : Path.Combine(home, "Library", "Fonts"),
            string.IsNullOrEmpty(windows) ? null : Path.Combine(windows, "Fonts"),
            string.IsNullOrEmpty(local) ? null : Path.Combine(local, "Microsoft", "Windows", "Fonts"),
        };
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 5 };
        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.ttf", options))
                    index.TryAdd(Path.GetFileName(file).ToLowerInvariant(), file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // pasta ilegível: segue com as outras
            }
        }
        return index;
    }
}
