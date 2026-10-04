using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cime.ExecutionAgent;

/// <summary>
/// Consumo FINAL da sessão lido do transcript do Claude Code (0044) — o mesmo cálculo do <c>usage</c> da skill
/// (<c>skills/analisar-bug/scripts/usage_scan.py</c>): respostas únicas por id, tokens somados, chamadas ao PRMake pelo
/// MCP × pelo script e (0055) de onde a sessão LEU — engenharia reversa × base antiga/KC × código (confirmação de item ×
/// exploração) × buscas, com os tokens estimados de cada resultado (tamanho ÷ 4). A skill manda fotos no meio da
/// execução; o executor manda a final quando o processo termina.
/// </summary>
public static partial class TranscriptUsage
{
    public static SessionUsage? Read(string sessionId, string? host)
    {
        try
        {
            var projects = Path.Combine(Paths.ClaudeHome, "projects");
            if (!Directory.Exists(projects)) return null;
            var path = Directory.EnumerateDirectories(projects).Select(d => Path.Combine(d, sessionId + ".jsonl")).FirstOrDefault(File.Exists);
            return path is null ? null : Parse(File.ReadLines(path), sessionId, host);
        }
        catch (Exception e)
        {
            Log.Warn($"não consegui ler o transcript da sessão {sessionId}: {e.Message}");
            return null;
        }
    }

    private static readonly string[] SourceKeys = ["re", "base", "code-confirm", "code-explore", "code-search"];

    public static SessionUsage? Parse(IEnumerable<string> lines, string sessionId, string? host)
    {
        var usage = new Dictionary<string, JsonElement>();
        var modelOf = new Dictionary<string, string>();
        var mcp = new HashSet<string>();
        var script = new HashSet<string>();
        var kb = new HashSet<string>();
        var search = new HashSet<string>();
        var calls = new Dictionary<string, (string? Source, string? File)>();
        var cited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stats = SourceKeys.ToDictionary(k => k, k => new ReadSource { Key = k });
        var explored = new Dictionary<string, ExploredFile>(StringComparer.OrdinalIgnoreCase);
        string? model = null;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonElement root;
            try { root = JsonDocument.Parse(line).RootElement; }
            catch (JsonException) { continue; }
            var type = Str(root, "type");
            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) continue;
            var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array ? c : default;

            if (type == "user" && content.ValueKind == JsonValueKind.Array)
            {
                // 0055: o resultado de cada chamada medida — tokens estimados e, da engenharia reversa, os arquivos citados.
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object || Str(part, "type") != "tool_result") continue;
                    if (Str(part, "tool_use_id") is not { } toolId || !calls.TryGetValue(toolId, out var call) || call.Source is null) continue;
                    var text = ResultText(part);
                    var source = call.Source;
                    if (source == "context")
                    {
                        var block = ReverseBlock().Match(text);
                        if (!block.Success) continue;
                        text = block.Value; source = "re";
                        stats["re"].Calls++;
                    }
                    if (source == "re")
                        foreach (Match m in Evidence().Matches(text))
                            cited.Add(Path.GetFileName(m.Groups[1].Value.Replace('\\', '/')));
                    var tokens = Tokens(text);
                    stats[source].Tokens += tokens;
                    if (source == "code-explore")
                    {
                        var key = FileKey(call.File ?? "?");
                        if (key.Length > 200) key = key[^200..];
                        if (!explored.TryGetValue(key, out var e)) explored[key] = e = new ExploredFile { Path = key };
                        e.Reads++; e.Tokens += tokens;
                    }
                }
                continue;
            }
            if (type != "assistant") continue;

            if (content.ValueKind == JsonValueKind.Array)
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object || Str(part, "type") != "tool_use") continue;
                    var name = Str(part, "name") ?? string.Empty;
                    var id = Str(part, "id") ?? Guid.NewGuid().ToString();
                    if (calls.ContainsKey(id)) continue;
                    if (name.StartsWith("mcp__prmake__", StringComparison.Ordinal)) mcp.Add(id);
                    else if (name == "Bash" && part.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object
                             && (Str(input, "command") ?? string.Empty).Contains("prmake-plan.sh", StringComparison.Ordinal))
                        script.Add(id);
                    var (source, file) = Classify(name, part);
                    // 0055: confirmação = o arquivo que um item da engenharia reversa já lido cita no Onde:
                    if (source == "code-read")
                        source = cited.Contains(Path.GetFileName(file!.Replace('\\', '/'))) ? "code-confirm" : "code-explore";
                    calls[id] = (source, file);
                    // 0045: kbCalls/searchCalls continuam (comparativos antigos)
                    if (source is "re" or "base") kb.Add(id);
                    else if (source == "code-search") search.Add(id);
                    if (source is not null && stats.TryGetValue(source, out var st)) st.Calls++;
                }

            if (!message.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) continue;
            // O Claude Code grava a mesma resposta em várias linhas (streaming): vale a última.
            var usageKey = Str(message, "id") ?? Str(root, "uuid") ?? Guid.NewGuid().ToString();
            usage[usageKey] = u.Clone();
            if (Str(message, "model") is { Length: > 0 } current && current != "<synthetic>") model = current;
            // 0047: cada resposta no modelo que a gerou ("<synthetic>" = mensagem local do Claude Code, sem custo).
            if (Str(message, "model") is { Length: > 0 } m2 && m2 != "<synthetic>") modelOf[usageKey] = m2;
        }
        if (usage.Count == 0) return null;
        long Sum(string name) => usage.Values.Sum(v => Long(v, name));
        return new SessionUsage
        {
            SessionId = sessionId,
            Host = host,
            Turns = usage.Count,
            InputTokens = Sum("input_tokens"),
            OutputTokens = Sum("output_tokens"),
            CacheReadTokens = Sum("cache_read_input_tokens"),
            CacheWriteTokens = Sum("cache_creation_input_tokens"),
            Model = model,
            Models = usage.Where(e => modelOf.ContainsKey(e.Key))
                .GroupBy(e => modelOf[e.Key].Split('[')[0])
                .Select(g => new ModelTokens
                {
                    Model = g.Key,
                    Turns = g.Count(),
                    InputTokens = g.Sum(e => Long(e.Value, "input_tokens")),
                    OutputTokens = g.Sum(e => Long(e.Value, "output_tokens")),
                    CacheReadTokens = g.Sum(e => Long(e.Value, "cache_read_input_tokens")),
                    CacheWriteTokens = g.Sum(e => Long(e.Value, "cache_creation_input_tokens"))
                })
                .ToList(),
            McpCalls = mcp.Count,
            ScriptCalls = script.Count,
            KbCalls = kb.Count,
            SearchCalls = search.Count,
            Sources = SourceKeys.Select(k => stats[k]).ToList(),
            ExploredFiles = explored.Values.OrderByDescending(e => e.Tokens).Take(10).ToList()
        };
    }

    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var x) ? x : 0;

    public static long Tokens(string text) => (text.Length + 3) / 4;

    private static string ResultText(JsonElement part)
    {
        if (!part.TryGetProperty("content", out var content)) return string.Empty;
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? string.Empty;
        if (content.ValueKind != JsonValueKind.Array) return string.Empty;
        return string.Join("\n", content.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object && Str(x, "type") == "text")
            .Select(x => Str(x, "text") ?? string.Empty));
    }

    private const string CodeExt = "(?:asp|aspx|ascx|asa|asax|inc|cs|cshtml|razor|vb|ts|tsx|js|jsx|mjs|html|htm|scss|css|sql|py|java|kt|go|xml|config|yml|yaml|sh|ps1|vue|json)";

    [GeneratedRegex(@"([\w@~.\\/-]*[\w-]\." + CodeExt + @")\b(?::\d+(?:-\d+)?)?", RegexOptions.IgnoreCase)]
    private static partial Regex Evidence();

    [GeneratedRegex(@"=== ENGENHARIA REVERSA.*?(?=\n=== |\z)", RegexOptions.Singleline)]
    private static partial Regex ReverseBlock();

    [GeneratedRegex(@"[A-Z]{2,4}-\d{1,4}")]
    private static partial Regex ReverseRef();

    [GeneratedRegex(@"(^|[\s|;&(])(grep|rg|ag|find|ack)\s")]
    private static partial Regex SearchCommand();

    [GeneratedRegex(@"(^|[\s|;&(])(cat|sed|head|tail|less|more|awk|nl)\s")]
    private static partial Regex ReadCommand();

    private static readonly string[] NotCode = ["/.claude/", "/.prmake/", "solvace-kb", "/tmp/", "/private/", "/scratchpad/", "/anexos", "/.t00", "/skills/"];
    private static readonly string[] NotCodeExt = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".bmp", ".svg", ".ico", ".zip"];
    private static readonly string[] CardFiles = ["dados/", "anexos/", "anexos-prmake/", "analise", "plano", "./"];

    private static bool IsCodePath(string p)
    {
        if (string.IsNullOrEmpty(p)) return false;
        var low = p.ToLowerInvariant().Replace('\\', '/');
        return !NotCode.Any(low.Contains) && !NotCodeExt.Any(low.EndsWith) && !CardFiles.Any(low.StartsWith)
               && !Path.GetFileName(low).StartsWith("prmake", StringComparison.Ordinal);
    }

    /// <summary>O mesmo arquivo lido com caminho relativo e absoluto (ou de outro worktree) conta junto.</summary>
    private static string FileKey(string p)
    {
        var parts = p.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? p : string.Join('/', parts.TakeLast(3));
    }

    /// <summary>
    /// (origem, arquivo) da chamada — re | base | code-read (vira confirm/explore) | code-explore | code-search | context
    /// (só o bloco da engenharia reversa do contexto conta) | null (não é leitura medida). Mesmas regras do usage_scan.py.
    /// </summary>
    public static (string? Source, string? File) Classify(string name, JsonElement part)
    {
        var input = part.TryGetProperty("input", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
        string Get(string n) => input.ValueKind == JsonValueKind.Object ? Str(input, n) ?? string.Empty : string.Empty;
        var command = Get("command");
        var path = Get("file_path") is { Length: > 0 } fp ? fp : Get("path") is { Length: > 0 } pp ? pp : Get("notebook_path");
        // 0052: a base pelo MCP (o caminho preferido da skill); get de item (modulo#RN-012) = engenharia reversa.
        if (name.StartsWith("mcp__prmake__prmake_base", StringComparison.Ordinal))
        {
            if (!name.EndsWith("_get", StringComparison.Ordinal)) return ("re", null);
            var refs = Get("refs").Split(',', StringSplitOptions.TrimEntries);
            return (refs.Any(r => r.Contains('#') || (ReverseRef().Match(r) is { Success: true } m && m.Length == r.Length
                                                       && !r.StartsWith("ART-", StringComparison.OrdinalIgnoreCase))) ? "re" : "base", null);
        }
        if (name == "Bash")
        {
            if (Regex.IsMatch(command, @"\bcontexto\b") && (command.Contains("prmake-plan", StringComparison.Ordinal) || command.Contains("$PLAN", StringComparison.Ordinal)))
                return ("context", null);
            if (command.Contains("kb.sh", StringComparison.Ordinal) || Regex.IsMatch(command, @"\$\{?KB\b"))
                return (Regex.IsMatch(command, @"(kb\.sh|\$\{?KB\}?)\S*\s+re\b") ? "re" : "base", null);
            if (command.Contains("/re.sh", StringComparison.Ordinal))
                return (Regex.IsMatch(command, @"re\.sh\S*\s+(get|find|impact|armadilhas|ids|status)\b") ? "re" : null, null);
            if (command.Contains("solvace-kb", StringComparison.Ordinal))
                return (command.Contains("/reverse/", StringComparison.Ordinal) || command.Contains("-re-", StringComparison.Ordinal) ? "re" : "base", null);
            if (SearchCommand().IsMatch(command) && !command.Contains("/.claude/", StringComparison.Ordinal) && !command.Contains("prmake-", StringComparison.Ordinal))
                return ("code-search", null);
            if (ReadCommand().IsMatch(command))
                foreach (Match m in Evidence().Matches(command))
                {
                    if (m.Index > 0 && command[m.Index - 1] is '$' or '}') continue;  // $D/x.sh: variável, não um arquivo do produto
                    var f = m.Groups[1].Value;
                    if (IsCodePath(f) && (f.Contains('/') || f.Contains('\\'))) return ("code-read", f);
                }
            return (null, null);
        }
        if (name is "Grep" or "Glob")
            return (path.Contains("/.claude/", StringComparison.Ordinal) ? null : "code-search", null);
        if (name is "Read" or "NotebookRead")
        {
            if (path.Contains("solvace-kb", StringComparison.Ordinal))
                return (path.Contains("/reverse/", StringComparison.Ordinal) || Path.GetFileName(path).Contains("-re-", StringComparison.Ordinal) ? "re" : "base", null);
            return IsCodePath(path) ? ("code-read", path) : (null, null);
        }
        if (name is "Agent" or "Task" && string.Equals(Get("subagent_type"), "explore", StringComparison.OrdinalIgnoreCase))
            return ("code-explore", "(subagente Explore)");
        return (null, null);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
