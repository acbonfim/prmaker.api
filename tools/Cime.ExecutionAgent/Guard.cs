using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cime.ExecutionAgent;

/// <summary>
/// Perfil do Claude Code no modo headless (0039): sem prompt de permissão (<c>--permission-mode dontAsk</c>), mas com
/// cerca — liberado o que a skill usa, negado merge/aprovação, push forçado ou em branch protegida, escrita em banco e
/// Cognito. O hook <c>PreToolUse</c> chama <c>prmake-agent guard</c> como segunda checagem.
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
            w.WriteStartObject();
            w.WriteString("matcher", "Bash");
            w.WriteStartArray("hooks");
            w.WriteStartObject();
            w.WriteString("type", "command");
            w.WriteString("command", guard);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        if (!File.Exists(target) || File.ReadAllText(target) != json)
            File.WriteAllText(target, json);
        return target;
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
        string? command = null;
        try
        {
            using var doc = JsonDocument.Parse(Console.In.ReadToEnd());
            if (doc.RootElement.TryGetProperty("tool_input", out var input) && input.TryGetProperty("command", out var c))
                command = c.GetString();
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
