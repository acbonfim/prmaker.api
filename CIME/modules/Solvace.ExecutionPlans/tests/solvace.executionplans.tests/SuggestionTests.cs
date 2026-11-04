using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0077: sugestões do assistente do card — respondem uma vez, falha pode tentar de novo.</summary>
public class SuggestionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");

    private static ExecutionSuggestion New() => new(Guid.NewGuid(), "75812", ExecutionSuggestionKind.LogHours, "log-hours:montar-analise",
        "Lançar 1h em Completed?", "texto", [new() { Key = "1h", Label = "Lançar 1h", Primary = true }, new() { Key = "skip", Label = "Agora não" }], Now);

    [Fact]
    public void Answer_once()
    {
        var s = New();
        s.Resolve("1h", true, "Horas lançadas", "Alex", Now);
        Assert.Equal((ExecutionSuggestionStatus.Done, "1h"), (s.Status, s.ChosenAction));
        Assert.Throws<DomainException>(() => s.Resolve("skip", true, null, "Alex", Now));
    }

    [Fact]
    public void Failure_can_be_retried()
    {
        var s = New();
        s.Resolve("1h", false, "DevOps recusou", "Alex", Now);
        Assert.Equal(ExecutionSuggestionStatus.Failed, s.Status);
        s.Reopen();
        Assert.True(s.IsOpen);
    }

    [Fact]
    public void Unknown_action_is_rejected() => Assert.Throws<DomainException>(() => New().Resolve("3h", true, null, "Alex", Now));

    [Fact]
    public void Dismissed_is_not_open()
    {
        var s = New();
        s.Dismiss("Alex", Now);
        Assert.Equal(ExecutionSuggestionStatus.Dismissed, s.Status);
    }
}
