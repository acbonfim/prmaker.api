using solvace.governance.application;
using solvace.governance.domain;
using solvace.governance.domain.Entities;
using Xunit;

namespace solvace.governance.tests;

/// <summary>0106: lições aprendidas — reforço por semelhança, limites, o que entra no que os agentes leem e os sinais.</summary>
public class LessonsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static GovernanceLesson Lesson(string trigger, string text, params string[] scope) =>
        new(new LessonData { Trigger = trigger, Text = text, Why = "card 79047", Scope = scope.ToList() }, LessonOrigin.Agent,
            new LessonSource { Card = "79047", Ref = "#1174" }, "Claude", Now);

    [Fact]
    public void The_same_mistake_told_in_other_words_is_similar_enough_to_reinforce()
    {
        var seeded = Lesson("a pessoa pede a análise inicial do card (bug ou User Story)",
            "inicie a análise do padrão com prmake_analysis_start; nunca monte um item de análise à mão");
        var again = Lesson("pediram a analise inicial do card",
            "use prmake_analysis_start para a analise do padrao, nunca monte item de analise a mao");
        Assert.True(GovernanceLesson.Similarity(seeded.SimilarityText, again.SimilarityText) >= GovernanceLesson.SameLessonSimilarity);

        var other = Lesson("levar um ajuste para um destino cujo PR já foi mesclado",
            "cherry-pick na mesma branch do destino e PR novo dela");
        Assert.True(GovernanceLesson.Similarity(seeded.SimilarityText, other.SimilarityText) < GovernanceLesson.SameLessonSimilarity);
    }

    [Fact]
    public void Reinforce_counts_keeps_the_last_ten_sources_and_appends_new_evidence()
    {
        var l = Lesson("x situação de teste", "y lição de teste");
        for (var i = 0; i < 12; i++)
            l.Reinforce(new LessonSource { Card = $"{80000 + i}" }, i == 0 ? "de novo no 80000" : null, "Claude", Now.AddMinutes(i));
        Assert.Equal(13, l.Hits);
        Assert.Equal(GovernanceLesson.MaxSources, l.Sources.Count);
        Assert.Equal("80011", l.Sources[^1].Card);
        Assert.Contains("de novo no 80000", l.Why);
    }

    [Theory]
    [InlineData("pode mesclar o PR quando o CI passar")]
    [InlineData("Liberado aprovar o PR do próprio card")]
    [InlineData("pode pedir a senha do ambiente na conversa")]
    public void A_lesson_never_loosens_the_fixed_rules(string text) =>
        Assert.Throws<DomainException>(() => Lesson("quando o PR estiver verde", text));

    [Fact]
    public void A_lesson_needs_trigger_text_and_valid_scope()
    {
        Assert.Throws<DomainException>(() => Lesson("", "faça x"));
        Assert.Throws<DomainException>(() => Lesson("quando y", " "));
        Assert.Throws<DomainException>(() => Lesson("quando y", "faça x", "Especialista Do Caso!"));
        var all = Lesson("quando y", "faça x", "all", "especialista-do-caso");
        Assert.Equal(["especialista-do-caso"], all.Scope);
    }

    [Fact]
    public void Scope_filters_and_old_single_lessons_stop_being_injected()
    {
        var specialist = Lesson("quando y", "faça x", "especialista-do-caso");
        Assert.True(specialist.AppliesTo("especialista-do-caso"));
        Assert.False(specialist.AppliesTo("executar-item-de-trabalho"));
        Assert.True(specialist.AppliesTo(null));
        Assert.True(Lesson("quando y", "faça x").AppliesTo("analisar-bug"));

        Assert.True(specialist.IsLive(Now.AddDays(100)));
        Assert.False(specialist.IsLive(Now.AddDays(121)));
        specialist.Reinforce(null, null, "Claude", Now.AddDays(1));
        Assert.True(specialist.IsLive(Now.AddDays(400)));
        specialist.Retire("não vale mais", "Alex", Now);
        Assert.False(specialist.IsLive(Now));
    }

    [Fact]
    public void Markdown_lists_lessons_with_code_and_recurrence_and_the_open_signals()
    {
        var l = Lesson("a pessoa pede a análise inicial", "use prmake_analysis_start.", "especialista-do-caso");
        l.Reinforce(null, null, "Claude", Now);
        var s = new GovernanceLessonSignal("79047", LessonSignalKind.ItemCancelled, "item-cancelled:1174", "O item #1174 foi cancelado 2 min depois.", "#1174", Now);
        var md = GovernanceLessonsApplication.Markdown([l], [s]);
        Assert.Contains("## Lições aprendidas", md);
        Assert.Contains("**Quando** a pessoa pede a análise inicial → use prmake_analysis_start (`L0` · 2× [especialista-do-caso])", md);
        Assert.Contains("## Sinais de erro deste card", md);
        Assert.Contains("(item-cancelled, #1174): O item #1174 foi cancelado 2 min depois.", md);
        Assert.Equal(string.Empty, GovernanceLessonsApplication.Markdown([], []));
    }

    [Fact]
    public void A_signal_is_learned_or_dismissed_with_a_reason()
    {
        var s = new GovernanceLessonSignal("79047", "inventado", "k", "aconteceu", null, Now);
        Assert.Equal(LessonSignalKind.Other, s.Kind);
        Assert.Throws<DomainException>(() => s.Dismiss(" ", "Claude", Now));
        s.Dismiss("a pessoa mudou de ideia", "Claude", Now);
        Assert.Equal(LessonSignalStatus.Dismissed, s.Status);
        s.Learned(7, "Claude", Now);
        Assert.Equal(LessonSignalStatus.Learned, s.Status);
        Assert.Equal(7, s.LessonId);
    }
}
