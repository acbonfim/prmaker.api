using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Filtering;

/// <summary>
/// Blocos que a IA devolve no guia, no "aprender com um card" e na análise a fundo (0038): o texto é markdown longo,
/// com ```mermaid``` e aspas — dentro de JSON o modelo erra o escape com frequência. Cada bloco vem entre
/// <c>&lt;&lt;&lt;TAG</c> e <c>TAG&gt;&gt;&gt;</c>, com campos <c>chave: valor</c> no começo, uma linha <c>---</c> e o corpo.
/// </summary>
public static class ArchitectureBlocks
{
    public record Block(IReadOnlyDictionary<string, string> Fields, string Body)
    {
        public string? Field(string name) => Fields.TryGetValue(name, out var v) && v.Length > 0 ? v : null;
    }

    private static readonly Regex FieldLine = new(@"^([a-zA-Z][a-zA-Z_-]*)\s*:\s*(.*)$", RegexOptions.Compiled);

    /// <summary>Formato pedido ao modelo, para colar no prompt.</summary>
    public static string Format(string tag, params string[] fields) =>
        $"<<<{tag}\n{string.Join("\n", fields.Select(f => f + ": ..."))}\n---\n(texto em markdown)\n{tag}>>>";

    public static List<Block> Parse(string? content, string tag)
    {
        var blocks = new List<Block>();
        if (string.IsNullOrWhiteSpace(content)) return blocks;
        var rx = new Regex($@"<<<{Regex.Escape(tag)}[ \t]*\r?\n(.*?)\r?\n[ \t]*{Regex.Escape(tag)}>>>", RegexOptions.Singleline);
        foreach (Match match in rx.Matches(content))
        {
            var lines = match.Groups[1].Value.Replace("\r\n", "\n").Split('\n');
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var i = 0;
            for (; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---") { i++; break; }
                var m = FieldLine.Match(lines[i]);
                if (!m.Success) break;
                fields[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim();
            }
            blocks.Add(new Block(fields, string.Join("\n", lines.Skip(i)).Trim()));
        }
        return blocks;
    }

    /// <summary>Primeiro bloco da tag (ou null).</summary>
    public static Block? First(string? content, string tag) => Parse(content, tag).FirstOrDefault();
}
