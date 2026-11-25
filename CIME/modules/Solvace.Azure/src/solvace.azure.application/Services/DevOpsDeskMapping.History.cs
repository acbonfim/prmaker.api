using System.Text.Json;
using solvace.azure.domain.Models;

namespace solvace.azure.application.Services;

/// <summary>
/// 0099 (modal único do card): aba Histórico — <c>…/workItems/{id}/updates</c> do DevOps em revisões legíveis (quem, quando,
/// campo antes → depois, links incluídos/removidos e o comentário da revisão), mais novas primeiro.
/// </summary>
public static partial class DevOpsDeskMapping
{
    /// <summary>Campos que o DevOps muda em toda revisão (ou que repetem outro) — fora do histórico.</summary>
    private static readonly HashSet<string> HistoryNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Rev", "System.AuthorizedDate", "System.RevisedDate", "System.ChangedDate", "System.Watermark", "System.AuthorizedAs",
        "System.ChangedBy", "System.PersonId", "System.CommentCount", "System.History", "System.Id", "System.AreaId", "System.IterationId",
        "System.NodeName", "System.TeamProject", "System.AreaLevel1", "System.IterationLevel1", "System.IsDeleted",
    };

    /// <summary>Campo de board de um time (<c>WEF_…</c>): repete o System.BoardColumn/BoardLane — fica de fora.</summary>
    private static bool IsHistoryNoise(string field) =>
        HistoryNoise.Contains(field) || field.StartsWith("WEF_", StringComparison.OrdinalIgnoreCase)
        || field.StartsWith("System.AreaLevel", StringComparison.OrdinalIgnoreCase)
        || field.StartsWith("System.IterationLevel", StringComparison.OrdinalIgnoreCase);

    /// <summary>Quantas revisões vieram numa página de <c>…/updates</c> (antes do filtro de ruído).</summary>
    public static int HistoryPageCount(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.GetArrayLength() : 0;

    public static IReadOnlyList<DevOpsHistoryEntry> ParseHistory(JsonElement root)
    {
        var list = new List<DevOpsHistoryEntry>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var u in arr.EnumerateArray())
        {
            var rev = u.TryGetProperty("rev", out var r) && r.TryGetInt32(out var rv) ? rv : 0;
            var changes = new List<DevOpsHistoryChange>();
            string? comment = null;
            DateTimeOffset? date = null;
            DevOpsPersonInfo? by = PersonOf(u, "revisedBy");
            if (u.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
                foreach (var f in fields.EnumerateObject())
                {
                    var oldV = f.Value.TryGetProperty("oldValue", out var ov) ? HistoryValue(ov) : null;
                    var newV = f.Value.TryGetProperty("newValue", out var nv) ? HistoryValue(nv) : null;
                    if (f.Name.Equals("System.ChangedDate", StringComparison.OrdinalIgnoreCase) && Str(f.Value, "newValue") is { } cd)
                        date = DateTimeOffset.TryParse(cd, out var d) ? d : date;
                    if (f.Name.Equals("System.ChangedBy", StringComparison.OrdinalIgnoreCase) && by is null
                        && f.Value.TryGetProperty("newValue", out var cb))
                        by = cb.ValueKind == JsonValueKind.Object ? PersonFromObject(cb) : newV is { Length: > 0 } n ? new DevOpsPersonInfo(n, string.Empty, string.Empty, null) : null;
                    if (f.Name.Equals("System.History", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(newV)) comment = newV;
                        continue;
                    }
                    if (IsHistoryNoise(f.Name) || (oldV is null && newV is null) || oldV == newV) continue;
                    changes.Add(new DevOpsHistoryChange(f.Name, oldV, newV));
                }
            if (date is null && DateOf(u, "revisedDate") is { Year: < 9999 } revised) date = revised;

            var added = new List<DevOpsHistoryLink>();
            var removed = new List<DevOpsHistoryLink>();
            if (u.TryGetProperty("relations", out var rels) && rels.ValueKind == JsonValueKind.Object)
            {
                HistoryLinks(rels, "added", added);
                HistoryLinks(rels, "removed", removed);
            }
            if (changes.Count == 0 && added.Count == 0 && removed.Count == 0 && comment is null) continue;
            list.Add(new DevOpsHistoryEntry(rev, by, date, rev == 1, changes, added, removed, comment));
        }
        return list.OrderByDescending(e => e.Rev).ToList();
    }

    private static void HistoryLinks(JsonElement rels, string kind, List<DevOpsHistoryLink> into)
    {
        if (!rels.TryGetProperty(kind, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var x in arr.EnumerateArray())
        {
            var url = Str(x, "url") ?? string.Empty;
            var name = x.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Object ? Str(a, "name") : null;
            into.Add(new DevOpsHistoryLink(Str(x, "rel") ?? string.Empty, WorkItemIdFromUrl(url), url, name));
        }
    }

    private static DevOpsPersonInfo? PersonFromObject(JsonElement p)
    {
        var display = Str(p, "displayName")?.Trim();
        return string.IsNullOrEmpty(display) ? null : new DevOpsPersonInfo(display, Str(p, "uniqueName") ?? string.Empty, Str(p, "id") ?? string.Empty, Str(p, "imageUrl"));
    }

    /// <summary>Valor de um campo na revisão: identidade → nome; número/bool como texto; HTML como veio (a tela tira as tags).</summary>
    private static string? HistoryValue(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Object => Str(v, "displayName") ?? v.GetRawText(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => v.GetRawText(),
    };

    /// <summary>
    /// Campo da coluna do board em que o card aparece: cada board tem o seu <c>WEF_&lt;id&gt;_Kanban.Column</c>; o do time do
    /// card tem <c>WEF_&lt;id&gt;_System.ExtensionMarker = true</c> (empate: o que bate com System.BoardColumn).
    /// </summary>
    public static string? ActiveBoardColumnField(IReadOnlyDictionary<string, JsonElement> fields)
    {
        const string suffix = "_Kanban.Column";
        string? Text(string k) => fields.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var boardColumn = Text("System.BoardColumn");
        return fields.Keys
            .Where(k => k.StartsWith("WEF_", StringComparison.Ordinal) && k.EndsWith(suffix, StringComparison.Ordinal))
            .Select(k => new
            {
                Field = k,
                Active = fields.TryGetValue(k[..^suffix.Length] + "_System.ExtensionMarker", out var m) && m.ValueKind == JsonValueKind.True,
                Current = Text(k)
            })
            .OrderByDescending(c => c.Active)
            .ThenByDescending(c => string.Equals(c.Current, boardColumn, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()?.Field;
    }

    /// <summary>
    /// Ordem para procurar o board de um card: o time com o nome de um nível da área (o último primeiro), depois o time padrão,
    /// depois os outros.
    /// </summary>
    public static IEnumerable<DevOpsNamedRef> TeamsByArea(IReadOnlyList<DevOpsNamedRef> teams, string area, string? defaultTeam)
    {
        var levels = area.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse().ToList();
        int Rank(DevOpsNamedRef t)
        {
            var i = levels.FindIndex(l => l.Equals(t.Name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) return i;
            if (levels.Any(l => l.Contains(t.Name, StringComparison.OrdinalIgnoreCase) || t.Name.Contains(l, StringComparison.OrdinalIgnoreCase))) return 50;
            return string.Equals(t.Name, defaultTeam, StringComparison.OrdinalIgnoreCase) ? 60 : 100;
        }
        return teams.OrderBy(Rank).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }
}
