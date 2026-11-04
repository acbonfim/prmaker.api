using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Responses;

/// <summary>
/// 0075: HTML do resumo da situação para a discussion do DevOps (publicado sozinho pelo PRMake). Converte só o markdown
/// que o resumo usa — parágrafos, listas com "-", **negrito**, *itálico*, `código` e links — com tudo escapado. Links
/// <c>ref:</c> (fotografia de código) viram código e imagens <c>anexo:</c> saem (não abrem fora do PRMake).
/// </summary>
public static partial class SituationHtml
{
    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])")]
    private static partial Regex Italic();

    public static string Build(CardSituationResponse situation, DateTimeOffset now, TimeZoneInfo zone) => Build(situation.Markdown, now, zone);

    /// <summary>0077: o texto (gerado ou editado pela pessoa) com o rodapé de quando foi atualizado.</summary>
    public static string Build(string markdown, DateTimeOffset now, TimeZoneInfo zone)
    {
        var sb = new StringBuilder();
        sb.Append("<div>");
        sb.Append(ToHtml(markdown));
        var at = TimeZoneInfo.ConvertTime(now, zone).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        sb.Append($"<p><i>Resumo da situação atualizado automaticamente pelo PRMake em {at}.</i></p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    public static string ToHtml(string? markdown)
    {
        var sb = new StringBuilder();
        var paragraph = new List<string>();
        var inList = false;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            sb.Append("<p>").Append(string.Join("<br>", paragraph.Select(Inline))).Append("</p>");
            paragraph.Clear();
        }
        void CloseList()
        {
            if (!inList) return;
            sb.Append("</ul>");
            inList = false;
        }

        foreach (var raw in (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0)
            {
                FlushParagraph();
                CloseList();
                continue;
            }
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                FlushParagraph();
                if (!inList) { sb.Append("<ul>"); inList = true; }
                sb.Append("<li>").Append(Inline(trimmed[2..])).Append("</li>");
                continue;
            }
            CloseList();
            paragraph.Add(trimmed.StartsWith('#') ? $"**{trimmed.TrimStart('#').Trim()}**" : line);
        }
        FlushParagraph();
        CloseList();
        return sb.ToString();
    }

    private static string Inline(string text)
    {
        // Links e código saem do texto antes de escapar (marcadores) para não escapar os delimitadores deles.
        var tokens = new List<string>();
        string Hold(string html) { tokens.Add(html); return $"\u0001{tokens.Count - 1}\u0002"; }

        var t = Image().Replace(text, "");
        t = Code().Replace(t, m => Hold($"<code>{Escape(m.Groups[1].Value)}</code>"));
        t = Link().Replace(t, m =>
        {
            var label = m.Groups[1].Value;
            var url = m.Groups[2].Value;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return Hold($"<a href=\"{Escape(url)}\">{InlineText(label)}</a>");
            // ref:id (fotografia de código) e qualquer outro esquema: só o texto.
            return Hold($"<code>{Escape(label.Trim('`'))}</code>");
        });
        t = InlineText(t);
        for (var i = tokens.Count - 1; i >= 0; i--)
            t = t.Replace($"\u0001{i}\u0002", tokens[i]);
        return t;
    }

    /// <summary>Só o que precisa (acentos ficam legíveis no HTML do DevOps).</summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string InlineText(string text)
    {
        var t = Escape(text);
        t = Bold().Replace(t, "<b>$1</b>");
        t = Italic().Replace(t, "<i>$1</i>");
        return t;
    }
}
