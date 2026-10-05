using solvace.knowledge.domain.Entities;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>
/// 0056: bugs achados ao testar a aba "Armadilhas" — "origin"/"mode" nulo (campo opcional de uma requisição real)
/// derrubava com NullReferenceException porque o "!" não troca null pelo valor default em tempo de execução.
/// </summary>
public class ReverseNullOriginModeTests
{
    [Fact]
    public void Trap_with_null_origin_defaults_to_manual_instead_of_throwing()
    {
        var trap = new ReverseTrap("legado-rca", "A3 não avança de etapa", "texto", null, null, origin: null, needsReview: false, "dev", DateTimeOffset.UtcNow);
        Assert.Equal("manual", trap.Origin);
    }

    [Fact]
    public void Trap_with_unknown_origin_also_defaults_to_manual()
    {
        var trap = new ReverseTrap("legado-rca", "t", "texto", null, null, origin: "nao-existe", needsReview: false, "dev", DateTimeOffset.UtcNow);
        Assert.Equal("manual", trap.Origin);
    }

    [Theory]
    [InlineData("migrated")]
    [InlineData(" MIGRATED ")]
    public void Trap_with_a_valid_origin_keeps_it_normalized(string origin)
    {
        var trap = new ReverseTrap("legado-rca", "t", "texto", null, null, origin, needsReview: false, "dev", DateTimeOffset.UtcNow);
        Assert.Equal("migrated", trap.Origin);
    }

    [Fact]
    public void Revision_mode_null_defaults_to_new_instead_of_throwing()
    {
        Assert.Equal("new", ReverseRevisionMode.Normalize(null));
    }

    [Fact]
    public void Revision_mode_unknown_also_defaults_to_new()
    {
        Assert.Equal("new", ReverseRevisionMode.Normalize("nao-existe"));
    }
}
