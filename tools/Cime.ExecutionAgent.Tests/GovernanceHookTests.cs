using System.Diagnostics;
using System.Text.Json;
using Cime.ExecutionAgent;
using Xunit;

namespace Cime.ExecutionAgent.Tests;

/// <summary>
/// 0098 (1.5.0): o settings do executor ganha o hook de governança do PRMake (PreToolUse em todas as ferramentas,
/// PostToolUse e Stop) e mantém o <c>prmake-agent guard</c> como rede de segurança sem rede.
/// </summary>
[Collection("env")]
public class GovernanceHookTests : IDisposable
{
    private readonly string _agentHome = Directory.CreateTempSubdirectory("agent-0098-").FullName;
    private readonly string _claudeHome = Directory.CreateTempSubdirectory("claude-0098-").FullName;
    private readonly string _python;

    public GovernanceHookTests()
    {
        _python = Path.Combine(_agentHome, OperatingSystem.IsWindows() ? "python.exe" : "python3");
        File.WriteAllText(_python, "");
        Environment.SetEnvironmentVariable("PRMAKE_AGENT_HOME", _agentHome);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _claudeHome);
        Environment.SetEnvironmentVariable("PRMAKE_PYTHON", _python);
    }

    public void Dispose()
    {
        foreach (var v in new[] { "PRMAKE_AGENT_HOME", "CLAUDE_CONFIG_DIR", "PRMAKE_PYTHON", "PRMAKE_API_BASE", "PRMAKE_TOKEN", "PRMAKE_CARD" })
            Environment.SetEnvironmentVariable(v, null);
        try { Directory.Delete(_agentHome, true); Directory.Delete(_claudeHome, true); } catch (IOException) { }
    }

    private void InstallScript()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GovernanceHook.Script)!);
        File.WriteAllText(GovernanceHook.Script, "# prmake-governance.py\n");
    }

    private static List<(string? Matcher, string Command)> Hooks(JsonElement root, string evt) =>
        root.GetProperty("hooks").TryGetProperty(evt, out var groups)
            ? groups.EnumerateArray()
                .SelectMany(g => g.GetProperty("hooks").EnumerateArray()
                    .Select(h => (g.TryGetProperty("matcher", out var m) ? m.GetString() : null, h.GetProperty("command").GetString()!)))
                .ToList()
            : [];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settings_have_the_governance_hook_and_keep_the_guard(bool specialist)
    {
        InstallScript();
        using var doc = JsonDocument.Parse(File.ReadAllText(ClaudeSettings.Ensure(specialist)));
        var pre = Hooks(doc.RootElement, "PreToolUse");
        Assert.Contains(pre, h => h.Matcher == "Bash" && h.Command.EndsWith(" guard"));
        var gov = Assert.Single(pre, h => h.Command.Contains("prmake-governance.py"));
        Assert.Equal("*", gov.Matcher);
        Assert.Contains("--from-executor", gov.Command);
        Assert.EndsWith("|| true", gov.Command);   // script sumido não vira exit 2 (= barrar tudo)
        Assert.Contains(_python.Replace('\\', '/'), gov.Command);
        Assert.Contains(Hooks(doc.RootElement, "PostToolUse"), h => h.Matcher == "*" && h.Command == gov.Command);
        Assert.Contains(Hooks(doc.RootElement, "Stop"), h => h.Matcher is null && h.Command == gov.Command);
    }

    [Fact]
    public void Without_the_script_only_the_guard()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ClaudeSettings.Ensure()));
        var pre = Hooks(doc.RootElement, "PreToolUse");
        Assert.Single(pre);
        Assert.EndsWith(" guard", pre[0].Command);
        Assert.False(doc.RootElement.GetProperty("hooks").TryGetProperty("PostToolUse", out _));
        Assert.Null(GovernanceHook.Command());
    }

    [Fact]
    public void Without_python_only_the_guard()
    {
        InstallScript();
        Environment.SetEnvironmentVariable("PRMAKE_PYTHON", Path.Combine(_agentHome, "nao-existe"));
        Assert.Null(GovernanceHook.Command());
        Assert.DoesNotContain("prmake-governance.py", File.ReadAllText(ClaudeSettings.Ensure()));
    }

    [Theory]
    [InlineData("git push --force origin bugfix/78720-qa")]
    [InlineData("gh pr merge 12 --squash")]
    [InlineData("git push origin master")]
    [InlineData("sqlcmd -S x -Q \"delete from TB_X\"")]
    [InlineData("rm -rf ~")]
    public void Guard_still_blocks(string command) => Assert.NotNull(Guard.Check(command));

    [Fact]
    public void Blocked_event_goes_to_the_trail()
    {
        Environment.SetEnvironmentVariable("PRMAKE_CARD", "78720");
        var body = GovernanceReport.Body("git push --force " + new string('x', 900), "push forçado não é permitido", "s-1",
            new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.FromHours(-3)));
        using var doc = JsonDocument.Parse(body);
        var e = Assert.Single(doc.RootElement.EnumerateArray().ToList());
        Assert.Equal("PreToolUse", e.GetProperty("event").GetString());
        Assert.Equal("Bash", e.GetProperty("tool").GetString());
        Assert.Equal("blocked", e.GetProperty("outcome").GetString());
        Assert.Equal(new[] { "executor-guard" }, e.GetProperty("rules").EnumerateArray().Select(r => r.GetString()!).ToArray());
        Assert.Equal(78720, e.GetProperty("cardNumber").GetInt32());
        Assert.Equal("s-1", e.GetProperty("sessionId").GetString());
        Assert.Equal("2026-11-24T10:00:00.000-03:00", e.GetProperty("startedAt").GetString());
        Assert.True(e.GetProperty("input").GetString()!.Length <= 500);
        Assert.Equal("executor", e.GetProperty("source").GetString());
    }

    [Fact]
    public void Report_never_hangs_without_network()
    {
        Environment.SetEnvironmentVariable("PRMAKE_TOKEN", "tk");
        Environment.SetEnvironmentVariable("PRMAKE_API_BASE", "http://127.0.0.1:9/api/v1");
        var watch = Stopwatch.StartNew();
        GovernanceReport.Blocked("git push -f", "push forçado não é permitido", null);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }
}
