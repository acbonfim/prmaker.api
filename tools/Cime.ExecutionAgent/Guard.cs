using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cime.ExecutionAgent;

/// <summary>
/// Perfil do Claude Code no modo headless (0039): sem prompt de permissão (<c>--permission-mode dontAsk</c>), mas com
/// cerca — liberado o que a skill usa, negado merge/aprovação, push forçado ou em branch protegida, escrita em banco e
/// Cognito. O hook <c>PreToolUse</c> chama <c>prmake-agent guard</c> como segunda checagem.
/// 0098 (1.5.0): mais o hook de governança do PRMake (<see cref="GovernanceHook"/>) em PreToolUse (todas as ferramentas —
/// o script só consulta o PRMake quando as regras casam), PostToolUse e Stop (trilha); o guard continua como rede de
/// segurança sem rede.
/// </summary>
public static class ClaudeSettings
{
    /// <param name="specialist">
    /// 0080: perfil do Especialista do caso — sem o token pessoal do usuário (ele usa o token do caso, que só lê e
    /// conversa) e sem commit/push/abrir PR (o especialista não altera código nem plano).
    /// </param>
    public static string Ensure(bool specialist = false)
    {
        Directory.CreateDirectory(Paths.Root);
        var target = specialist ? Paths.CaseClaudeSettings : Paths.ClaudeSettings;
        var self = Environment.ProcessPath ?? Paths.Bin;
        var guard = Paths.IsWindows ? $"\"{self.Replace("\\", "/")}\" guard" : $"'{self.Replace("'", "'\\''")}' guard";
        var governance = GovernanceHook.Command();
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartObject("permissions");
            w.WriteStartArray("allow");
            foreach (var a in new[] { "Bash", "Read", "Edit", "Write", "Glob", "Grep", "Skill", "Agent", "Task", "TodoWrite", "WebFetch", "WebSearch", "NotebookEdit", "mcp__prmake" })
                w.WriteStringValue(a);
            w.WriteEndArray();
            w.WriteStartArray("deny");
            foreach (var d in new[]
                     {
                         "Bash(gh pr merge:*)", "Bash(gh pr review:*)", "Bash(git push --force:*)", "Bash(git push -f:*)",
                         "Bash(git push --force-with-lease:*)", "Bash(rm -rf /:*)", "Bash(rm -rf ~:*)",
                         // 0092: senhas dos ambientes da validação — só o navegador da skill lê (nunca o Claude).
                         "Read(~/.claude/solvace-env-credentials.json)", "Edit(~/.claude/solvace-env-credentials.json)"
                     }.Concat(specialist
                         ? ["Read(~/.claude/prmake-token.txt)", "Edit(~/.claude/prmake-token.txt)", "Bash(git push:*)", "Bash(git commit:*)", "Bash(gh pr create:*)"]
                         : []))
                w.WriteStringValue(d);
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteStartObject("hooks");
            w.WriteStartArray("PreToolUse");
            HookGroup(w, "Bash", guard, null);
            if (governance is not null) HookGroup(w, "*", governance, GovernanceHook.TimeoutSeconds);
            w.WriteEndArray();
            if (governance is not null)
            {
                w.WriteStartArray("PostToolUse");
                HookGroup(w, "*", governance, GovernanceHook.TimeoutSeconds);
                w.WriteEndArray();
                w.WriteStartArray("Stop");
                HookGroup(w, null, governance, GovernanceHook.TimeoutSeconds);
                w.WriteEndArray();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        if (!File.Exists(target) || File.ReadAllText(target) != json)
            File.WriteAllText(target, json);
        return target;
    }

    private static void HookGroup(Utf8JsonWriter w, string? matcher, string command, int? timeoutSeconds)
    {
        w.WriteStartObject();
        if (matcher is not null) w.WriteString("matcher", matcher);
        w.WriteStartArray("hooks");
        w.WriteStartObject();
        w.WriteString("type", "command");
        w.WriteString("command", command);
        if (timeoutSeconds is { } t) w.WriteNumber("timeout", t);
        w.WriteEndObject();
        w.WriteEndArray();
        w.WriteEndObject();
    }
}

/// <summary>
/// 0098: hook de governança do PRMake (<c>prmake-governance.py</c>, instalado com as skills em
/// <c>~/.claude/skills/.prmake/</c>). No executor ele roda com <c>--from-executor</c> e o pedido leva
/// <c>PRMAKE_GOVERNANCE_HOOK=executor</c>: o mesmo hook vindo do <c>~/.claude/settings.json</c> da pessoa sai sem repetir.
/// Sem o script ou sem Python 3 não entra (o guard continua).
/// </summary>
public static class GovernanceHook
{
    public const int TimeoutSeconds = 10;
    public const string EnvName = "PRMAKE_GOVERNANCE_HOOK";

    public static string Script => Path.Combine(Paths.ClaudeHome, "skills", ".prmake", "prmake-governance.py");

    /// <summary>Comando do hook, ou null sem o script/Python. "|| true": script sumido faria o python sair com 2 (= barrar).</summary>
    public static string? Command()
    {
        var script = Script;
        if (!File.Exists(script)) return null;
        var python = Python();
        if (python is null) return null;
        return Paths.IsWindows
            ? $"\"{python.Replace('\\', '/')}\" \"{script.Replace('\\', '/')}\" --from-executor || true"
            : $"{Quote(python)} {Quote(script)} --from-executor || true";
    }

    private static string Quote(string s) => $"'{s.Replace("'", "'\\''")}'";

    /// <summary>Python 3 da máquina (caminho absoluto: o serviço do executor não tem o PATH do terminal). PRMAKE_PYTHON vale por cima.</summary>
    public static string? Python()
    {
        if (Environment.GetEnvironmentVariable("PRMAKE_PYTHON") is { Length: > 0 } forced)
            return File.Exists(forced) ? forced : null;
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (Paths.IsWindows)
        {
            // O python.exe de WindowsApps é o atalho da Microsoft Store (não roda).
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var installed = Directory.Exists(Path.Combine(local, "Programs", "Python"))
                ? Directory.GetDirectories(Path.Combine(local, "Programs", "Python"), "Python3*").OrderByDescending(d => d, StringComparer.Ordinal)
                : Enumerable.Empty<string>();
            return path.Where(d => !d.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)).Concat(installed)
                .Select(d => Path.Combine(d, "python.exe")).FirstOrDefault(File.Exists);
        }
        // macOS: /usr/bin/python3 sem as ferramentas de linha de comando abre a janela de instalação a cada chamada.
        var macStubOk = !Paths.IsMac || Directory.Exists("/Library/Developer/CommandLineTools") || Directory.Exists("/Applications/Xcode.app");
        return path.Concat(["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"])
            .Select(d => Path.Combine(d, "python3"))
            .Where(p => p != "/usr/bin/python3" || macStubOk)
            .FirstOrDefault(File.Exists);
    }
}

/// <summary>
/// 0098: bloqueio do <see cref="Guard"/> reportado à governança do PRMake (<c>POST Governance/hooks/events</c>, regra
/// <c>executor-guard</c>) — a trilha do admin mostra também o que o executor barrou sem rede. Nunca trava: sem token,
/// sem rede ou passou de 3 s, só desiste.
/// </summary>
public static class GovernanceReport
{
    public static void Blocked(string command, string reason, string? sessionId)
    {
        try
        {
            var token = ConfigStore.UserToken();
            if (token is null) return;
            var apiBase = Environment.GetEnvironmentVariable("PRMAKE_API_BASE") is { Length: > 0 } b
                ? b.TrimEnd('/')
                : ConfigStore.ResolveApiBase(ConfigStore.Load());
            using var http = new HttpClient { BaseAddress = new Uri(apiBase + "/"), Timeout = TimeSpan.FromSeconds(3) };
            http.DefaultRequestHeaders.Add("x-api-key", token);
            using var content = new StringContent(Body(command, reason, sessionId, DateTimeOffset.Now), System.Text.Encoding.UTF8, "application/json");
            using var response = http.PostAsync("Governance/hooks/events", content).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                Log.Warn($"governança: bloqueio do guard não reportado (HTTP {(int)response.StatusCode})");
        }
        catch (Exception e)
        {
            Log.Warn($"governança: bloqueio do guard não reportado ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>Lote de um evento (sem serialização por reflexão — executor trimmed).</summary>
    public static string Body(string command, string reason, string? sessionId, DateTimeOffset at)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartArray();
            w.WriteStartObject();
            w.WriteString("event", "PreToolUse");
            w.WriteString("tool", "Bash");
            w.WriteString("input", command.Length > 500 ? command[..499] + "…" : command);
            w.WriteString("startedAt", at.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteNumber("durationMs", 0);
            w.WriteString("outcome", "blocked");
            w.WriteStartArray("rules");
            w.WriteStringValue("executor-guard");
            w.WriteEndArray();
            w.WriteString("detail", $"Bloqueado pelo executor do PRMake: {reason}");
            if (int.TryParse((Environment.GetEnvironmentVariable("PRMAKE_CARD") ?? "").TrimStart('#'), out var card) && card > 0)
                w.WriteNumber("cardNumber", card);
            if (!string.IsNullOrWhiteSpace(sessionId)) w.WriteString("sessionId", sessionId);
            w.WriteString("machine", Paths.HostName);
            w.WriteString("source", Environment.GetEnvironmentVariable("PRMAKE_SPECIALIST") == "1" ? "specialist" : "executor");
            w.WriteEndObject();
            w.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}

public static partial class Guard
{
    private const string Protected = @"(master|main|develop|dev|homolog\S*|release\S*|production|prod|staging)";

    [GeneratedRegex(@"\bgh\s+pr\s+(merge|review)\b|\bgh\s+api\b[^|;&]*/pulls/\d+/(merge|reviews)\b|\bgit\s+merge\s+--no-ff\s+\S+\s*&&\s*git\s+push", RegexOptions.IgnoreCase)]
    private static partial Regex MergeOrApprove();

    [GeneratedRegex(@"\bgit\s+push\b[^|;&]*\s(--force(-with-lease)?|-f)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForcePush();

    [GeneratedRegex(@"\bgit\s+push\b(?<rest>[^|;&]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Push();

    [GeneratedRegex(@"^(\+?(refs/heads/)?" + Protected + @"|\S*:(refs/heads/)?" + Protected + @")$", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedRef();

    [GeneratedRegex(@"\b(sqlcmd|psql|mysql|sql-query\.(sh|py)|mongosh?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DbClient();

    [GeneratedRegex(@"\b(insert\s+into|update\s+\S+\s+set|delete\s+from|drop\s+(table|database|schema)|truncate\s+|alter\s+table|merge\s+into|exec(ute)?\s+sp_)", RegexOptions.IgnoreCase)]
    private static partial Regex DbWrite();

    [GeneratedRegex(@"\baws\s+cognito-idp\s+(admin-(create|delete|update|set|disable|enable|reset|add|remove|confirm|link)|sign-up|delete|update|create|set)", RegexOptions.IgnoreCase)]
    private static partial Regex CognitoWrite();

    [GeneratedRegex(@"\brm\s+-(rf|fr)\s+(/|~|\$HOME)(\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Wipe();

    /// <summary>0092: arquivo das senhas dos ambientes (validação com prints) — só o script da skill lê.</summary>
    [GeneratedRegex(@"solvace-env-credentials", RegexOptions.IgnoreCase)]
    private static partial Regex EnvCredentials();

    /// <summary>Hook PreToolUse: lê o JSON do stdin; exit 2 + stderr bloqueia o comando e diz o motivo ao Claude.</summary>
    public static int Run()
    {
        string? command = null, sessionId = null;
        try
        {
            using var doc = JsonDocument.Parse(Console.In.ReadToEnd());
            if (doc.RootElement.TryGetProperty("tool_input", out var input) && input.TryGetProperty("command", out var c))
                command = c.GetString();
            if (doc.RootElement.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.String)
                sessionId = sid.GetString();
        }
        catch (JsonException)
        {
            return 0;
        }
        if (string.IsNullOrWhiteSpace(command)) return 0;

        var reason = Check(command);
        if (reason is null) return 0;
        Console.Error.WriteLine($"Bloqueado pelo executor do PRMake: {reason}. Registre no plano o que precisa de uma pessoa e siga com o resto.");
        Log.Console = false;
        Log.Warn($"guard bloqueou: {reason} — {(command.Length > 200 ? command[..200] : command)}");
        // 0098: a governança do PRMake também vê o que o guard barrou (sem travar se a rede falhar).
        GovernanceReport.Blocked(command, reason, sessionId);
        return 2;
    }

    [GeneratedRegex(@"prmake-token|\bgit\s+(push|commit)\b|\bgh\s+pr\s+(create|edit|close)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpecialistForbidden();

    public static string? Check(string command)
    {
        // 0080: o Especialista do caso só lê e conversa — nada de token pessoal, commit, push ou PR.
        if (Environment.GetEnvironmentVariable("PRMAKE_SPECIALIST") == "1" && SpecialistForbidden().IsMatch(command))
            return "o Especialista do caso não usa o token pessoal nem altera código/PR — proponha a mudança na conversa (prmake_case_propose)";
        if (MergeOrApprove().IsMatch(command))
            return "merge e aprovação de PR são sempre de uma pessoa (o Claude só abre PRs)";
        if (ForcePush().IsMatch(command))
            return "push forçado não é permitido";
        foreach (Match m in Push().Matches(command))
        {
            var tokens = m.Groups["rest"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !t.StartsWith('-')).ToList();
            // git push <remote> <refspec...>: a partir do segundo argumento são refs.
            if (tokens.Skip(1).Any(t => ProtectedRef().IsMatch(t.Trim('"', '\''))))
                return "push direto em branch protegida (master/main/develop/release...) não é permitido — use a branch de correção e abra PR";
        }
        if (DbClient().IsMatch(command) && DbWrite().IsMatch(command))
            return "escrita em banco não é permitida (análise e correção são somente leitura no banco)";
        if (CognitoWrite().IsMatch(command))
            return "alteração no Cognito não é permitida";
        if (Wipe().IsMatch(command))
            return "comando destrutivo";
        if (EnvCredentials().IsMatch(command))
            return "o arquivo de senhas dos ambientes só é lido pelo navegador da validação (env.sh) — nunca abra, copie ou mostre a senha";
        return null;
    }
}
