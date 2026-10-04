using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0055: de onde a análise leu — engenharia reversa × base × código, por sessão, plano e linha de base.</summary>
public class ReadSourcesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-04T12:00:00Z");

    private static ExecutionPlan Plan(string phase = ExecutionPhase.Analysis) =>
        new("75091", "bug", "Não finaliza", null, null, "dev", Now, phase, phase == ExecutionPhase.Correction ? Guid.NewGuid() : null);

    private static ExecutionReadSource S(string key, int calls, long tokens) => new() { Key = key, Calls = calls, Tokens = tokens };

    private static void Record(ExecutionPlan plan, string session, IReadOnlyCollection<ExecutionReadSource>? sources, IReadOnlyCollection<ExecutionExploredFile>? explored = null) =>
        plan.RecordUsage(session, "mac", 10, 100, 50, 1000, 200, "claude-opus-5-5", Now, 1, 0, 2, 1, null, sources, explored);

    [Fact]
    public void Plan_usage_shows_what_was_read_from_the_reverse_engineering_and_the_explored_files()
    {
        var plan = Plan();
        Record(plan, "s1",
            [S("re", 3, 2100), S("BASE", 1, 400), S("code-confirm", 2, 900), S("code-explore", 1, 5800), S("code-search", 2, 300), S("outra", 9, 9)],
            [new() { Path = "systems/sa3/sa3_ajax.asp", Reads = 1, Tokens = 5800 }]);

        var usage = plan.FillSummary(new ExecutionPlanSummaryResponse(), 0, 0).Usage!;
        Assert.Equal(["re", "base", "code-confirm", "code-explore", "code-search"], usage.Sources.Select(s => s.Key));   // origem desconhecida fora
        Assert.Equal(2100d / 9500, usage.ReverseShare!.Value, 3);
        Assert.Equal("systems/sa3/sa3_ajax.asp", Assert.Single(usage.ExploredFiles).Path);
    }

    [Fact]
    public void Old_skill_without_sources_keeps_what_the_session_had_and_old_sessions_show_nothing()
    {
        var plan = Plan();
        Record(plan, "s1", [S("re", 1, 100)]);
        Record(plan, "s1", null);                                     // executor antigo: não apaga a medição
        Assert.Equal(100, Assert.Single(plan.NetReadSources()).Tokens);

        var old = Plan();
        Record(old, "s2", null);
        var usage = old.FillSummary(new ExecutionPlanSummaryResponse(), 0, 0).Usage!;
        Assert.Empty(usage.Sources);
        Assert.Null(usage.ReverseShare);
    }

    [Fact]
    public void Correction_in_the_same_session_does_not_count_what_the_analysis_already_read()
    {
        var analysis = Plan();
        Record(analysis, "s1", [S("re", 3, 2000), S("code-explore", 1, 500)], [new() { Path = "a.asp", Reads = 1, Tokens = 500 }]);

        var correction = Plan(ExecutionPhase.Correction);
        correction.SetSessionBaseline("s1", analysis.Sessions.Single(), Now);
        Record(correction, "s1", [S("re", 4, 2300), S("code-explore", 3, 2500)],
            [new() { Path = "a.asp", Reads = 1, Tokens = 500 }, new() { Path = "b.cs", Reads = 2, Tokens = 2000 }]);

        var net = correction.NetReadSources();
        Assert.Equal((1, 300L), (net.Single(s => s.Key == "re").Calls, net.Single(s => s.Key == "re").Tokens));
        Assert.Equal((2, 2000L), (net.Single(s => s.Key == "code-explore").Calls, net.Single(s => s.Key == "code-explore").Tokens));
        Assert.Equal("b.cs", Assert.Single(correction.NetExploredFiles()).Path);   // a.asp já era da análise
    }

    [Fact]
    public void Negative_values_are_rejected_and_explored_files_keep_the_heaviest_ten()
    {
        var plan = Plan();
        Assert.Throws<DomainException>(() => Record(plan, "s1", [S("re", -1, 10)]));
        var many = Enumerable.Range(1, 15).Select(i => new ExecutionExploredFile { Path = $"f{i}.cs", Reads = 1, Tokens = i * 100 }).ToList();
        Record(plan, "s1", [S("code-explore", 15, 12000)], many);
        var files = plan.NetExploredFiles();
        Assert.Equal(10, files.Count);
        Assert.Equal("f15.cs", files[0].Path);
    }
}
