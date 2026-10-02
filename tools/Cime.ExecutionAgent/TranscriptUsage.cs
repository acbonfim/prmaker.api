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
        var mcp = new HashSet<string>();
        var script = new HashSet<string>();
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
                }

            if (!message.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) continue;
            // O Claude Code grava a mesma resposta em várias linhas (streaming): vale a última.
            usage[Str(message, "id") ?? Str(root, "uuid") ?? Guid.NewGuid().ToString()] = u.Clone();
            model = Str(message, "model") ?? model;
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
            McpCalls = mcp.Count,
            ScriptCalls = script.Count
        };
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
