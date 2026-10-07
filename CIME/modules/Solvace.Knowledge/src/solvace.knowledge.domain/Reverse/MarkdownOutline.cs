using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Reverse;

/// <summary>Pedaço de um markdown grande (0070): posição em code points, IDs dos itens que contém e altura estimada (px).</summary>
public sealed record MarkdownChunk(int Index, int Start, int Length, List<string> Ids, int Estimate);

/// <summary>
/// Divide um markdown grande em pedaços para a tela buscar sob demanda (0070) — a MESMA regra do <c>splitMarkdown</c> do
/// front: pedaços de ~<c>target</c> caracteres cortados antes de um cabeçalho (fora de bloco de código); seção sem
/// cabeçalho maior que 3× o alvo é cortada numa linha em branco. Posições em code points (o <c>substring</c> do PostgreSQL).
/// </summary>
public static partial class MarkdownOutline
{
    public const int DefaultTarget = 30_000;
    private const int LinePx = 23;
    private const int CharsPerLine = 100;

    public static List<MarkdownChunk> Split(string? content, int target = DefaultTarget)
    {
        var chunks = new List<MarkdownChunk>();
        if (string.IsNullOrEmpty(content)) return chunks;
        var ids = new List<string>();
        int chunkStart = 0, position = 0, size = 0, lines = 0;
        double height = 0;
        var fence = false;

        void Flush()
        {
            if (lines == 0) return;
            chunks.Add(new MarkdownChunk(chunks.Count, chunkStart, position - chunkStart, ids, Math.Max((int)Math.Round(height), 40)));
            ids = [];
            chunkStart = position;
            size = 0;
            lines = 0;
            height = 0;
        }

        var index = 0;
        while (index <= content.Length)
        {
            var newline = content.IndexOf('\n', index);
            var end = newline < 0 ? content.Length : newline;
            var raw = content[index..end];
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            var trimmed = line.TrimStart();
            var isFence = trimmed.StartsWith("```", StringComparison.Ordinal);
            var heading = !fence && !isFence && HeadingLine().IsMatch(line);
            if (heading && size >= target) Flush();
            else if (!fence && !isFence && size >= target * 3 && trimmed.Length == 0) Flush();
            lines++;
            size += line.Length + 1;
            height += LineHeight(line, heading, fence);
            if (heading && ItemHeading().Match(line) is { Success: true } m) ids.Add(m.Groups[1].Value);
            // abre/fecha bloco de código; ```mermaid``` inline (abre e fecha na mesma linha) não conta
            if (isFence && (fence || trimmed.IndexOf("```", 3, StringComparison.Ordinal) < 0)) fence = !fence;
            position += CodePointLength(content, index, newline < 0 ? content.Length : newline + 1);
            if (newline < 0) break;
            index = newline + 1;
        }
        Flush();
        return chunks;
    }

    private static int CodePointLength(string text, int start, int end)
    {
        var count = 0;
        for (var i = start; i < end; i++)
            if (!(char.IsLowSurrogate(text[i]) && i > start && char.IsHighSurrogate(text[i - 1]))) count++;
        return count;
    }

    private static double LineHeight(string line, bool heading, bool fence)
    {
        if (heading) return 46;
        if (line.Trim().Length == 0) return fence ? LinePx : 6;
        return Math.Ceiling(line.Length / (double)CharsPerLine) * LinePx + (fence ? 0 : 4);
    }

    [GeneratedRegex(@"^#{1,4}\s")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^#{2,4}\s+[*`_]*([A-Z]{2,4}-\d{1,4})\b")]
    private static partial Regex ItemHeading();
}
