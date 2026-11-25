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
