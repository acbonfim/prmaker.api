using System.Text;
using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Reverse;

/// <summary>Um item de um documento da engenharia reversa: o bloco do cabeçalho com ID até o próximo cabeçalho de nível igual ou maior.</summary>
public sealed record ReverseItem(
    string Id,
    string Kind,
    string Title,
    int Level,
    int Order,
    string Body,
    List<string> Tags,
    List<string> Tables,
    List<string> Refs,
    List<string> Evidence,
    List<string> Modules,
    List<string> Synonyms)
{
    public bool Removed => Title.StartsWith("(removido)", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Cabeçalho do documento (fora de bloco de código).</summary>
public sealed record ReverseDocHeading(int Level, string Text, int Line);

/// <summary>
/// Lê os itens de um documento da engenharia reversa (0052): cabeçalhos <c>##</c>–<c>####</c> que começam pelo ID
/// (<c>### RN-012 — título</c>) e as linhas de metadados (<c>**Onde:**</c>, <c>**Tabelas:**</c>, <c>**Módulos:**</c>,
/// <c>**Tags:**</c>). Cabeçalhos dentro de blocos de código não contam.
/// </summary>
public static partial class ReverseDocParser
{
    public static List<ReverseDocHeading> Headings(string content) => Headings(Lines(content));

    private static List<ReverseDocHeading> Headings(string[] lines)
    {
        var result = new List<ReverseDocHeading>();
        var fence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            // Abre/fecha bloco de código; ```mermaid``` inline (abre e fecha na mesma linha) não conta.
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (fence || trimmed.IndexOf("```", 3, StringComparison.Ordinal) < 0) fence = !fence;
                continue;
            }
            if (fence) continue;
            var m = HeadingLine().Match(line);
            if (m.Success) result.Add(new ReverseDocHeading(m.Groups[1].Value.Length, m.Groups[2].Value.Trim(), i));
        }
        return result;
    }

    public static List<ReverseItem> Parse(string content)
    {
        var lines = Lines(content);
        var items = new List<ReverseItem>();
        foreach (var (heading, end, kind, number, title) in ItemBlocks(lines))
        {
            var body = string.Join('\n', lines[heading.Line..end]).TrimEnd();
            var canonical = ReverseItemKinds.Canonical(kind, number);
            items.Add(new ReverseItem(canonical, kind, title.Length == 0 ? canonical : Trim(title, 300), heading.Level, items.Count,
                body, MetaList(body, TagsLine()), Tables(body), Refs(body, canonical), Evidence(body), Modules(body), MetaList(body, SynonymsLine())));
        }
        return items;
    }

    /// <summary>
    /// O texto fora dos itens (introdução, cabeçalhos de seção, diagramas...), em tempo linear: as linhas cobertas por
    /// algum item saem. Antes era um <c>Replace</c> do corpo de cada item no documento inteiro — quadrático, e com
    /// milhares de itens em milhões de caracteres o GET da revisão estourava o tempo (504).
    /// </summary>
    public static string OutsideItems(string content)
    {
        var lines = Lines(content);
        var covered = new bool[lines.Length];
        var coveredUntil = 0;
        foreach (var (heading, end, _, _, _) in ItemBlocks(lines))
        {
            // Os blocos vêm em ordem de início; um item dentro de outro já está coberto.
            for (var i = Math.Max(heading.Line, coveredUntil); i < end; i++) covered[i] = true;
            coveredUntil = Math.Max(coveredUntil, end);
        }
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
            if (!covered[i]) sb.Append(lines[i]).Append('\n');
        return sb.ToString();
    }

    /// <summary>Cabeçalhos de item (com ID) e a linha onde cada bloco termina: o próximo cabeçalho de nível igual ou maior.</summary>
    private static IEnumerable<(ReverseDocHeading Heading, int End, string Kind, int Number, string Title)> ItemBlocks(string[] lines)
    {
        var headings = Headings(lines);
        // Fim de cada cabeçalho numa passada só (pilha): antes era uma busca à frente por cabeçalho.
        var ends = new int[headings.Count];
        var open = new Stack<int>();
        for (var h = 0; h < headings.Count; h++)
        {
            while (open.Count > 0 && headings[open.Peek()].Level >= headings[h].Level) ends[open.Pop()] = headings[h].Line;
            open.Push(h);
        }
        while (open.Count > 0) ends[open.Pop()] = lines.Length;

        for (var h = 0; h < headings.Count; h++)
        {
            var heading = headings[h];
            if (heading.Level < 2) continue;
            var id = ItemHeading().Match(Clean(heading.Text));
            if (!id.Success || !int.TryParse(id.Groups["num"].Value, out var number)) continue;
            var kind = id.Groups["kind"].Value.ToUpperInvariant();
            if (!ReverseItemKinds.IsKind(kind)) continue;
            yield return (heading, ends[h], kind, number, id.Groups["title"].Value.Trim());
        }
    }

    private static string[] Lines(string content) => (content ?? string.Empty).Replace("\r\n", "\n").Split('\n');

    private static string Clean(string heading) => heading.Replace("**", "").Replace("`", "").Trim();

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];

    private static List<string> MetaList(string body, Regex line)
    {
        var m = line.Match(body);
        if (!m.Success) return [];
        return m.Groups[1].Value.Split([',', ';', '·', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim().Trim('`', '*', '.', ' ').Trim())
            .Where(v => v.Length is > 0 and <= 80)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
    }

    /// <summary>Tabelas e objetos de banco citados (0053: views, procedures, functions, triggers — para o impacto).</summary>
    private static List<string> Tables(string body) =>
        TablePattern().Matches(body).Select(m => m.Value.ToUpperInvariant())
            .Concat(DbObjectPattern().Matches(body).Select(m => m.Groups[1].Value.ToUpperInvariant()))
            .Concat(MetaList(body, TablesLine()).Where(t => !t.Contains(' ')))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList();

    private static List<string> Modules(string body) =>
        MetaList(body, ModulesLine())
            .Select(v => ModuleKey().Match(v.ToLowerInvariant()))
            .Where(m => m.Success).Select(m => m.Value)
            .Distinct().Take(20).ToList();

    /// <summary>
    /// Evidência: arquivo:linha e, desde a 0053, o banco — <c>**Onde:** banco DEMO local · dbo.STP_X (linha 42)</c> ou
    /// <c>**Banco:** DEMO global · alterado em …</c> (regra que só existe no banco).
    /// </summary>
    private static List<string> Evidence(string body) =>
        EvidencePattern().Matches(body).Select(m => m.Value.Trim('`', '(', ')', ','))
            .Concat(BankEvidence().Matches(body).Select(m => "banco: " + m.Groups["v"].Value.Replace("**", "").Replace("`", "").Trim()))
            .Concat(AwsEvidence().Matches(body).Select(m => "aws: " + m.Groups["v"].Value.Replace("**", "").Replace("`", "").Trim()))
            .Select(v => v.Length <= 200 ? v : v[..200])
            .Distinct(StringComparer.Ordinal).Take(30).ToList();

    private static List<string> Refs(string body, string self)
    {
        var refs = new List<string>();
        foreach (Match m in RefPattern().Matches(body))
        {
            if (!int.TryParse(m.Groups["num"].Value, out var n)) continue;
            var kind = m.Groups["kind"].Value.ToUpperInvariant();
            if (!ReverseItemKinds.IsKind(kind)) continue;
            var id = ReverseItemKinds.Canonical(kind, n);
            var value = m.Groups["module"].Success ? $"{m.Groups["module"].Value.ToLowerInvariant()}#{id}" : id;
            if (value != self && !refs.Contains(value)) refs.Add(value);
        }
        return refs.Take(60).ToList();
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^(?<kind>[A-Za-z]{2,4})-(?<num>\d{1,4})\b\s*(?:[—–:\-]+\s*)?(?<title>.*)$")]
    private static partial Regex ItemHeading();

    [GeneratedRegex(@"\*\*Tags:?\*\*:?\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex TagsLine();

    [GeneratedRegex(@"\*\*Sin[ôo]nimos:?\*\*:?\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex SynonymsLine();

    /// <summary>0058: evidência de infraestrutura — <c>**Onde:** aws 367983645102/us-east-1 · lambda:nome</c> (recurso lido pelo AWS CLI).</summary>
    [GeneratedRegex(@"\*\*Onde:?\*\*:?[^\n]*?\b(?<v>aws\b[^\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AwsEvidence();

    [GeneratedRegex(@"\*\*Onde:?\*\*:?[^\n]*?\b(?<v>banco\b[^\n]+)|\*\*Banco:?\*\*:?\s*(?<v>[^\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BankEvidence();

    [GeneratedRegex(@"\b(?:dbo\.)?((?:VW|STP|SP|USP|FN|UFN|TR|TRG|PRC|PR)_[A-Za-z0-9_]{2,})\b")]
    private static partial Regex DbObjectPattern();

    [GeneratedRegex(@"\*\*Tabelas:?\*\*:?\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex TablesLine();

    [GeneratedRegex(@"\*\*M[óo]dulos:?\*\*:?\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ModulesLine();

    [GeneratedRegex(@"[a-z0-9][a-z0-9._-]*")]
    private static partial Regex ModuleKey();

    [GeneratedRegex(@"\bTB_[A-Za-z0-9_]+\b")]
    private static partial Regex TablePattern();

    [GeneratedRegex(@"[\w./\\-]+\.(?:cs|cshtml|razor|asp|aspx|ascx|inc|js|ts|tsx|html|scss|sql|py|json|ya?ml|xml|config|vb|java|go)(?::\d+(?:-\d+)?)?\b")]
    private static partial Regex EvidencePattern();

    [GeneratedRegex(@"(?:(?<module>[a-z0-9][a-z0-9._-]*)#)?\b(?<kind>TELA|PRF|EST|NTF|CFG|REL|TEC|CMP|API|EVT|JOB|INT|FLX|OBJ|PER|GLO|ADR|NFR|SEQ|GAP|SQL|TRG|INF|TUT|FAQ|FN|UC|RN|DB|UI)-(?<num>\d{1,4})\b")]
    private static partial Regex RefPattern();
}
