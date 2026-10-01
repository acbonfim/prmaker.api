using solvace.knowledge.application;
using solvace.knowledge.domain.Entities;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>0040: registro das perguntas do "Pergunte" e reforço das seções de operação.</summary>
public class ArchitectureQuestionTests
{
    [Fact]
    public void Same_question_with_other_accents_and_punctuation_is_the_same_record()
    {
        Assert.Equal(ArchitectureQuestion.NormalizeText("Como habilitar um módulo em uma planta?"),
            ArchitectureQuestion.NormalizeText("  como HABILITAR um modulo em uma planta  "));
    }

    [Fact]
    public void Answered_question_reopens_when_the_base_still_does_not_answer()
    {
        var now = DateTimeOffset.UtcNow;
        var q = new ArchitectureQuestion("Como habilitar um módulo?", "ana", now);
        q.Asked("operacao", "not-found", null, null, "ana", now);
        Assert.True(q.IsGap);
        Assert.Equal("operacao", q.Kind);
        q.Resolve("answered", "operacao-plataforma", "operacao", "publicada", "admin", now);
        Assert.Equal("answered", q.Status);
        q.Asked("operacao", "answered", "operacao-plataforma", "operacao", "bia", now.AddMinutes(1));
        Assert.Equal("answered", q.Status);
        Assert.Equal(2, q.Times);
        q.Asked("operacao", "not-found", null, null, "bia", now.AddMinutes(2));
        Assert.Equal("open", q.Status);
        Assert.Throws<DomainException>(() => q.Resolve("talvez", null, null, null, "admin", now));
    }

    [Fact]
    public void Operation_sections_win_when_boosted()
    {
        var p = new ArchitectureProject("revamp-x", "t", DateTimeOffset.UtcNow);
        p.Update("Revamp — X", "revamp", null, "Módulo X.", null, null, null, null, "t", DateTimeOffset.UtcNow);
        var tech = new ArchitectureSection(p.Id, "modulos");
        tech.Write("Módulos", "Para habilitar o módulo a tela de módulos grava IsActive.", 20, "skill", null, "t", DateTimeOffset.UtcNow);
        var op = new ArchitectureSection(p.Id, "operacao");
        op.Write("Configuração e operação", "Habilitar o módulo na planta: Administração → Módulos.", 85, "skill", null, "t", DateTimeOffset.UtcNow);
        p.Sections.Add(tech);
        p.Sections.Add(op);
        var terms = ArchitectureSearch.Terms("habilitar modulo");
        var plain = ArchitectureSearch.Run([p], [], terms, 5);
        var boosted = ArchitectureSearch.Run([p], [], terms, 5, null, new HashSet<string> { "operacao" });
        Assert.Equal("operacao", boosted[0].SectionKey);
        Assert.True(boosted.First(h => h.SectionKey == "operacao").Score > plain.First(h => h.SectionKey == "operacao").Score);
    }
}
