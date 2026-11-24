using Cime.ExecutionAgent;
using Xunit;

namespace Cime.ExecutionAgent.Tests;

/// <summary>0099 (ajustes 2): pedidos de épico (E…) rodam na pasta neutra da Mesa (~/.prmake/desk), sem workspace.</summary>
[Collection("env")]
public class DeskPathTests
{
    [Theory]
    [InlineData("E12", true)]
    [InlineData("e7", true)]
    [InlineData("#E345", true)]
    [InlineData(" E1 ", true)]
    [InlineData("78161", false)]
    [InlineData("E", false)]
    [InlineData("E12a", false)]
    [InlineData("EX12", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Epic_key_is_recognized_like_the_server(string? card, bool expected) =>
        Assert.Equal(expected, Paths.IsEpicKey(card));

    [Fact]
    public void Desk_root_lives_under_prmake_home_and_is_created()
    {
        var home = Path.Combine(Path.GetTempPath(), "prmake-desk-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PRMAKE_HOME", home);
        try
        {
            var desk = Paths.EnsureDeskRoot();
            Assert.Equal(Path.Combine(home, "desk"), desk);
            Assert.True(Directory.Exists(desk));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRMAKE_HOME", null);
            if (Directory.Exists(home)) Directory.Delete(home, true);
        }
    }
}
