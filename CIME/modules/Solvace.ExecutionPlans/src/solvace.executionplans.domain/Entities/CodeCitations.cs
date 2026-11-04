using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0075: citações de código num texto da análise — <c>caminho:linha</c>/<c>caminho:ini-fim</c> e links
/// <c>[texto](ref:id)</c>. Mesma regra da tela (helpers/markdown-ext.ts do front): fora de blocos de código e de URLs.
/// </summary>
public static partial class CodeCitations
{
    public record Citation(string Path, int Start, int End)
    {
        public override string ToString() => Start == End ? $"{Path}:{Start}" : $"{Path}:{Start}-{End}";
    }

    public const string Extensions = "asp|aspx|inc|cs|cshtml|ts|tsx|js|jsx|mjs|html|htm|css|scss|sql|json|java|py|sh|xml|vb|config|yml|yaml|md";

    [GeneratedRegex(@"(?<![\w/.\-:])((?:[\w.\-]+/)*[\w.\-]+\.(?:" + Extensions + @")):(\d{1,6})(?:-(\d{1,6}))?(?![\w])", RegexOptions.IgnoreCase)]
    private static partial Regex CitationPattern();

    [GeneratedRegex(@"\]\(ref:([A-Za-z0-9][A-Za-z0-9._\-]*)\)")]
    private static partial Regex RefLinkPattern();

    [GeneratedRegex(@"^[ \t]*(```|~~~).*?^[ \t]*\1[^\n]*$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex FencedBlock();

    [GeneratedRegex(@"\bhttps?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    /// <summary>Citações <c>caminho:linha</c> (sem repetir), na ordem em que aparecem.</summary>
    public static List<Citation> Extract(string? markdown)
    {
        var text = Clean(markdown);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<Citation>();
        foreach (Match m in CitationPattern().Matches(text))
        {
            var start = int.Parse(m.Groups[2].Value);
            var end = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : start;
            if (start < 1 || end < start) continue;
            var c = new Citation(m.Groups[1].Value, start, end);
            if (seen.Add(c.ToString()))
                list.Add(c);
        }
        return list;
    }

    /// <summary>Ids dos links <c>(ref:id)</c>, normalizados.</summary>
    public static List<string> RefLinks(string? markdown) =>
        RefLinkPattern().Matches(Clean(markdown)).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();

    private static string Clean(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;
        var text = FencedBlock().Replace(markdown.Replace("\r\n", "\n"), "");
        return Url().Replace(text, "");
    }
}
