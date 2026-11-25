using System.Text.Json;
using Cime.ExecutionAgent;
using Xunit;

namespace Cime.ExecutionAgent.Tests;

/// <summary>0102: anexos da pergunta da Base Solvace — gravados na pasta do pedido, sem sair dela; claim com effort e arquivos.</summary>
[Collection("env")]
public class BaseAskFilesTests
{
    [Fact]
    public void Files_are_written_in_the_request_folder_and_never_outside_it()
    {
        var home = Path.Combine(Path.GetTempPath(), "prmake-agent-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PRMAKE_AGENT_HOME", home);
        try
        {
            var dir = JobRunner.WriteFiles(Guid.NewGuid(), [
                new ClaimFile { Name = "erro.png", MediaType = "image/png", Data = Convert.ToBase64String([1, 2, 3]) },
                new ClaimFile { Name = "../../fora.txt", MediaType = "text/plain", Data = Convert.ToBase64String("oi"u8.ToArray()) },
                new ClaimFile { Name = "ruim.txt", MediaType = "text/plain", Data = "%%%" }
            ]);

            Assert.StartsWith(home, dir);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(dir, "erro.png")));
            Assert.True(File.Exists(Path.Combine(dir, "fora.txt")));
            Assert.False(File.Exists(Path.Combine(dir, "ruim.txt")));
            Assert.False(File.Exists(Path.Combine(home, "fora.txt")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRMAKE_AGENT_HOME", null);
            try { Directory.Delete(home, true); } catch { /* temporário */ }
        }
    }

    [Fact]
    public void Claim_reads_effort_and_files_without_reflection()
    {
        const string json = """
            {"request":{"id":"6f1c2a52-6a0e-4b8e-9a51-0b7c2f1e9d10","cardNumber":"BQ-0123456789AB","kind":"base-ask"},
             "freshPrompt":"p","resumePrompt":"p","model":"sonnet","effort":"low",
             "files":[{"name":"erro.png","mediaType":"image/png","data":"AQID"}],"caseToken":"t"}
            """;

        var claim = JsonSerializer.Deserialize(json, AgentJson.Default.ClaimResponse)!;

        Assert.Equal("low", claim.Effort);
        Assert.Equal("erro.png", Assert.Single(claim.Files!).Name);
        Assert.True(new JobRunner(new AgentConfig(), null!, claim).IsBaseAsk);
    }
}

/// <summary>1.9.1: pergunta da Base mais rápida — Claude enxuto e fila consultada mais cedo com o relay falhando.</summary>
[Collection("env")]
public class BaseAskFastTests
{
    [Fact]
    public void Question_runs_lean_without_the_person_settings_skills_or_extra_tools()
    {
        var quick = JobRunner.BaseAskArgs(false, "/tmp/s.json");
        Assert.Equal("project", quick[quick.IndexOf("--setting-sources") + 1]);
        Assert.Equal("Read", quick[quick.IndexOf("--tools") + 1]);
        Assert.Contains("--disable-slash-commands", quick);
        Assert.Contains("prmake_base_answer", quick[quick.IndexOf("--system-prompt") + 1]);
        Assert.Equal("Read,Grep,Glob", JobRunner.BaseAskArgs(true, "/tmp/s.json")[quick.IndexOf("--tools") + 1]);
    }

    [Fact]
    public void Base_profile_has_no_hooks()
    {
        var home = Path.Combine(Path.GetTempPath(), "prmake-agent-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PRMAKE_AGENT_HOME", home);
        try
        {
            var json = File.ReadAllText(ClaudeSettings.EnsureBaseAsk());
            Assert.DoesNotContain("hooks", json);
            Assert.Contains("mcp__prmake", json);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRMAKE_AGENT_HOME", null);
            try { Directory.Delete(home, true); } catch { /* temporário */ }
        }
    }

    [Fact]
    public void Relay_failing_makes_the_executor_poll_the_queue()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), Runner.IdleWait(true, new WorkerState()));
        Assert.Equal(TimeSpan.FromSeconds(10), Runner.IdleWait(true, new WorkerState { ReportEverySeconds = 15 }));
        Assert.Equal(TimeSpan.FromSeconds(10), Runner.IdleWait(false, new WorkerState()));
    }
}
