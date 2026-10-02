using System.Text.Json;

namespace Cime.ExecutionAgent;

/// <summary>
/// Consumo FINAL da sessão lido do transcript do Claude Code (0044) — o mesmo cálculo do <c>usage</c> da skill
/// (<c>prmake-plan.sh</c>): respostas únicas por id, tokens somados e chamadas ao PRMake pelo MCP × pelo script. A
/// skill manda fotos no meio da execução; o executor manda a final quando o processo termina.
/// </summary>
public static class TranscriptUsage
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

    public static SessionUsage? Parse(IEnumerable<string> lines, string sessionId, string? host)
    {
        var usage = new Dictionary<string, JsonElement>();
        var modelOf = new Dictionary<string, string>();
        var mcp = new HashSet<string>();
        var script = new HashSet<string>();
        var kb = new HashSet<string>();
        var search = new HashSet<string>();
        string? model = null;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonElement root;
            try { root = JsonDocument.Parse(line).RootElement; }
            catch (JsonException) { continue; }
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "assistant") continue;
            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) continue;

            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object || Str(part, "type") != "tool_use") continue;
                    var name = Str(part, "name") ?? string.Empty;
                    var id = Str(part, "id") ?? Guid.NewGuid().ToString();
                    if (name.StartsWith("mcp__prmake__", StringComparison.Ordinal)) mcp.Add(id);
                    else if (name == "Bash" && part.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object
                             && (Str(input, "command") ?? string.Empty).Contains("prmake-plan.sh", StringComparison.Ordinal))
                        script.Add(id);
                    // 0045: a Base Solvace veio antes das buscas no código? (mesma regra do usage da skill)
                    switch (Classify(name, part))
                    {
                        case "kb": kb.Add(id); break;
                        case "search": search.Add(id); break;
                    }
                }

            if (!message.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) continue;
            // O Claude Code grava a mesma resposta em várias linhas (streaming): vale a última.
            var key = Str(message, "id") ?? Str(root, "uuid") ?? Guid.NewGuid().ToString();
            usage[key] = u.Clone();
            if (Str(message, "model") is { Length: > 0 } current && current != "<synthetic>") model = current;
            // 0047: cada resposta no modelo que a gerou ("<synthetic>" = mensagem local do Claude Code, sem custo).
            if (Str(message, "model") is { Length: > 0 } m && m != "<synthetic>") modelOf[key] = m;
        }
        if (usage.Count == 0) return null;
        long Sum(string name) => usage.Values.Sum(u => u.TryGetProperty(name, out var v) && v.TryGetInt64(out var x) ? x : 0);
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
            SearchCalls = search.Count
        };
    }

    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var x) ? x : 0;

    private static readonly System.Text.RegularExpressions.Regex SearchCommand =
        new(@"(^|[\s|;&(])(grep|rg|ag|find|ack)\s", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>kb = consulta à Base Solvace (kb.sh ou o espelho); search = busca no código (Grep/Glob ou grep/rg/find no Bash).</summary>
    public static string? Classify(string name, JsonElement part)
    {
        var input = part.TryGetProperty("input", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
        string Get(string n) => input.ValueKind == JsonValueKind.Object ? Str(input, n) ?? string.Empty : string.Empty;
        var command = Get("command");
        var path = Get("file_path") + Get("path");
        if (command.Contains("kb.sh", StringComparison.Ordinal) || command.Contains("solvace-kb", StringComparison.Ordinal)
            || path.Contains("solvace-kb", StringComparison.Ordinal))
            return command.Contains("contexto", StringComparison.Ordinal) ? null : "kb";
        if (name is "Grep" or "Glob")
            return path.Contains("/.claude/", StringComparison.Ordinal) ? null : "search";
        if (name == "Bash" && SearchCommand.IsMatch(command) && !command.Contains("/.claude/", StringComparison.Ordinal)
            && !command.Contains("prmake-", StringComparison.Ordinal))
            return "search";
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
