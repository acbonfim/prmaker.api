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
