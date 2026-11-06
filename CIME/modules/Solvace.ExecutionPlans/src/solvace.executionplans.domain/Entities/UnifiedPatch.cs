using System.Text;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0091: lê a saída do <c>git diff</c> (vários arquivos) — um bloco por arquivo com caminho, situação
/// (modified/added/removed/renamed), +/- e o trecho a partir do primeiro <c>@@</c>.
/// </summary>
public static class UnifiedPatch
{
    public static List<WorkDeliverableDiffFile> Parse(string patch)
    {
        var files = new List<WorkDeliverableDiffFile>();
        if (string.IsNullOrEmpty(patch))
            return files;

        WorkDeliverableDiffFile? current = null;
        StringBuilder? body = null;
        void Flush()
        {
            if (current is null) return;
            current.Patch = body is { Length: > 0 } ? body.ToString().TrimEnd('\n') : null;
            files.Add(current);
        }

        foreach (var raw in patch.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                Flush();
                var (a, b) = Paths(line["diff --git ".Length..]);
                current = new WorkDeliverableDiffFile { Path = b ?? a ?? "?", PreviousPath = a != b ? a : null };
                body = null;
                continue;
            }
            if (current is null)
                continue;
            if (body is null)
            {
                if (line.StartsWith("new file mode", StringComparison.Ordinal)) current.Status = "added";
                else if (line.StartsWith("deleted file mode", StringComparison.Ordinal)) current.Status = "removed";
                else if (line.StartsWith("rename from ", StringComparison.Ordinal)) { current.Status = "renamed"; current.PreviousPath = line["rename from ".Length..]; }
                else if (line.StartsWith("rename to ", StringComparison.Ordinal)) current.Path = line["rename to ".Length..];
                else if (line.StartsWith("+++ ", StringComparison.Ordinal) && line != "+++ /dev/null" && line.Length > 6) current.Path = Strip(line[4..]);
                else if (line.StartsWith("@@", StringComparison.Ordinal)) body = new StringBuilder().Append(line).Append('\n');
                continue;
            }
            body.Append(line).Append('\n');
            if (line.StartsWith('+')) current.Additions++;
            else if (line.StartsWith('-')) current.Deletions++;
        }
        Flush();
        foreach (var f in files.Where(f => f.Status == "modified" && f.PreviousPath is not null && f.PreviousPath != f.Path))
            f.Status = "renamed";
        foreach (var f in files.Where(f => f.Status != "renamed"))
            f.PreviousPath = null;
        return files;
    }

    /// <summary><c>a/x b/y</c> (com ou sem prefixo, caminhos sem espaço ou entre aspas).</summary>
    private static (string? A, string? B) Paths(string rest)
    {
        if (rest.StartsWith('"'))
        {
            var end = rest.IndexOf('"', 1);
            if (end > 0)
                return (Strip(rest[1..end]), Strip(rest[(end + 1)..].Trim().Trim('"')));
        }
        var mid = rest.IndexOf(" b/", StringComparison.Ordinal);
        if (mid > 0)
            return (Strip(rest[..mid]), Strip(rest[(mid + 1)..]));
        var parts = rest.Split(' ', 2);
        return (Strip(parts[0]), parts.Length > 1 ? Strip(parts[1]) : null);
    }

    private static string Strip(string p)
    {
        p = p.Trim().Trim('"');
        return p.StartsWith("a/", StringComparison.Ordinal) || p.StartsWith("b/", StringComparison.Ordinal) ? p[2..] : p;
    }
}
