using Cime.ExecutionAgent;
using Xunit;

namespace Cime.ExecutionAgent.Tests;

/// <summary>0092: as senhas dos ambientes da validação só são lidas pelo navegador da skill — o Claude nunca abre o arquivo.</summary>
[Collection("env")]
public class EnvCredentialsGuardTests
{
    [Theory]
    [InlineData("cat ~/.claude/solvace-env-credentials.json")]
    [InlineData("jq . \"$HOME/.claude/solvace-env-credentials.json\"")]
    [InlineData("cp ~/.claude/solvace-env-credentials.json /tmp/x")]
    public void Claude_cannot_touch_the_password_file(string command) => Assert.NotNull(Guard.Check(command));

    [Theory]
    [InlineData("bash ~/.claude/skills/validar-ambiente/scripts/env.sh browser start qa --card 77434 --task 121")]
    [InlineData("bash ~/.claude/skills/validar-ambiente/scripts/env.sh browser fill qa-admin-ewcm-com-br \"#busca\" \"update plano set\"")]
    [InlineData("bash ~/.claude/skills/validar-ambiente/scripts/env.sh evidence 77434 120 121 ~/.prmake/cards/77434/validacao/qa-121")]
    public void Validation_commands_pass(string command) => Assert.Null(Guard.Check(command));

    [Fact]
    public void Settings_deny_reading_the_password_file()
    {
        var home = Directory.CreateTempSubdirectory("agent-0092-").FullName;
        Environment.SetEnvironmentVariable("PRMAKE_AGENT_HOME", home);
        try
        {
            Assert.Contains("Read(~/.claude/solvace-env-credentials.json)", File.ReadAllText(ClaudeSettings.Ensure()));
            Assert.Contains("Read(~/.claude/solvace-env-credentials.json)", File.ReadAllText(ClaudeSettings.Ensure(specialist: true)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRMAKE_AGENT_HOME", null);
            Directory.Delete(home, true);
        }
    }
}
