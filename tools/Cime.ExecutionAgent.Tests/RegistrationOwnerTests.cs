using System.Text;
using Cime.ExecutionAgent;
using Xunit;

namespace Cime.ExecutionAgent.Tests;

/// <summary>Dono do executor × dono da api-key da máquina (troca de pessoa na mesma máquina).</summary>
public class RegistrationOwnerTests
{
    private static string Jwt(string payloadJson)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"HS256\"}")}.{B64(payloadJson)}.assinatura";
    }

    [Fact]
    public void Reads_ExternalId_from_payload() =>
        Assert.Equal("8885cea1-ed3d-47b8-8390-796cdf679194", Registration.ExternalIdOf(Jwt("{\"ExternalId\":\"8885cea1-ed3d-47b8-8390-796cdf679194\",\"role\":[\"admin\"]}")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-jwt")]
    [InlineData("a.%%%.c")]
    public void Garbage_has_no_owner(string? token) => Assert.Null(Registration.ExternalIdOf(token));

    [Fact]
    public void Different_people_changed()
    {
        var executor = Jwt("{\"ExternalId\":\"8885cea1-ed3d-47b8-8390-796cdf679194\",\"executorId\":\"x\"}");
        var user = Jwt("{\"ExternalId\":\"f5694632-a709-44e5-b883-28288ccf57c9\"}");
        Assert.True(Registration.OwnerChanged(executor, user));
    }

    [Fact]
    public void Same_person_case_insensitive_not_changed()
    {
        var executor = Jwt("{\"ExternalId\":\"F5694632-A709-44E5-B883-28288CCF57C9\"}");
        var user = Jwt("{\"ExternalId\":\"f5694632-a709-44e5-b883-28288ccf57c9\"}");
        Assert.False(Registration.OwnerChanged(executor, user));
    }

    [Fact]
    public void Unknown_owner_is_not_a_change()
    {
        Assert.False(Registration.OwnerChanged(Jwt("{\"name\":\"x\"}"), Jwt("{\"ExternalId\":\"a\"}")));
        Assert.False(Registration.OwnerChanged(Jwt("{\"ExternalId\":\"a\"}"), null));
    }
}
