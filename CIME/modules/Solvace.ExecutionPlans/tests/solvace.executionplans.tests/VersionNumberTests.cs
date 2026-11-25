using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0074: número da versão no histórico do card (análise e correção contam separado, 1 = a mais antiga).</summary>
public class VersionNumberTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-01T10:00:00Z");

    private static ExecutionPlanSummaryResponse S(string phase, int minutes, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Phase = phase,
        CreatedAt = T0.AddMinutes(minutes)
    };

    [Fact]
    public void Numbers_each_phase_from_oldest_to_newest()
    {
        // Lista como o repositório devolve: do mais novo para o mais antigo.
        var a3 = S(ExecutionPhase.Analysis, 50);
        var c2 = S(ExecutionPhase.Correction, 40);
        var a2 = S(ExecutionPhase.Analysis, 30);
        var c1 = S(ExecutionPhase.Correction, 20);
        var a1 = S(ExecutionPhase.Analysis, 10);

        ExecutionPlanMappings.AssignVersionNumbers([a3, c2, a2, c1, a1]);

        Assert.Equal([1, 2, 3], new[] { a1.VersionNumber, a2.VersionNumber, a3.VersionNumber });
        Assert.Equal([1, 2], new[] { c1.VersionNumber, c2.VersionNumber });
    }

    [Fact]
    public void Same_creation_time_is_broken_by_id_so_numbering_is_stable()
    {
        var low = S(ExecutionPhase.Analysis, 5, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var high = S(ExecutionPhase.Analysis, 5, Guid.Parse("00000000-0000-0000-0000-000000000002"));

        ExecutionPlanMappings.AssignVersionNumbers([high, low]);
        Assert.Equal(1, low.VersionNumber);
        Assert.Equal(2, high.VersionNumber);

        ExecutionPlanMappings.AssignVersionNumbers([low, high]);
        Assert.Equal(1, low.VersionNumber);
        Assert.Equal(2, high.VersionNumber);
    }

    [Fact]
    public void Empty_list_is_fine()
    {
        ExecutionPlanMappings.AssignVersionNumbers([]);
    }
}
