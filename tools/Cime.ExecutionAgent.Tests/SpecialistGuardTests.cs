using Cime.ExecutionAgent;
using Xunit;

namespace Cime.ExecutionAgent.Tests;

/// <summary>0080: o Especialista do caso não usa o token pessoal nem mexe em código/PR (o executor bloqueia no hook).</summary>
[Collection("env")]
public class SpecialistGuardTests
{
    [Theory]
    [InlineData("cat ~/.claude/prmake-token.txt")]
    [InlineData("git commit -am 'fix'")]
    [InlineData("git push origin bugfix/77881-qa")]
    [InlineData("gh pr create --base qa")]
    public void Specialist_cannot(string command)
    {
        Environment.SetEnvironmentVariable("PRMAKE_SPECIALIST", "1");
        try
        {
            Assert.NotNull(Guard.Check(command));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRMAKE_SPECIALIST", null);
        }
    }

    [Fact]
    public void Main_lane_keeps_the_old_rules()
    {
        Environment.SetEnvironmentVariable("PRMAKE_SPECIALIST", null);
        Assert.Null(Guard.Check("git push origin bugfix/77881-qa"));
    }

    [Fact]
    public void Specialist_can_read()
    {
        Environment.SetEnvironmentVariable("PRMAKE_SPECIALIST", "1");
        try
        {
            Assert.Null(Guard.Check("git -C ~/repos/edv-solvace-api show origin/qa:api_util/Services/EnvironmentService.cs"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRMAKE_SPECIALIST", null);
        }
    }
}

/// <summary>0080 (1.2.1): o --mcp-config do especialista não pode depender de serialização por reflexão (executor trimmed).</summary>
public class CaseMcpConfigTests
{
    [Fact]
    public void Tests_run_like_the_published_executor() =>
        Assert.False(System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault);

    [Fact]
    public void Mcp_config_is_written_without_reflection()
    {
        var json = JobRunner.CaseMcpJson("https://api.example.com/mcp", "tok\"en");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var prmake = doc.RootElement.GetProperty("mcpServers").GetProperty("prmake");
        Assert.Equal("http", prmake.GetProperty("type").GetString());
        Assert.Equal("https://api.example.com/mcp", prmake.GetProperty("url").GetString());
        Assert.Equal("tok\"en", prmake.GetProperty("headers").GetProperty("x-api-key").GetString());
    }
}
