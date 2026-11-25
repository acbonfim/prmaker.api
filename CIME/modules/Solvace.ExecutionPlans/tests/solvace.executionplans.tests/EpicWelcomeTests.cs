using solvace.executionplans.application;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>Apresentação do especialista na conversa do épico recém-criado (texto montado sem IA).</summary>
public class EpicWelcomeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T12:00:00Z");

    private static Epic New(string? team = "Product Development Team", string? board = "Boards") =>
        new("Monitorar Triage Sustain", null, null, null, team, board, null, Guid.NewGuid(), "Alex", Now);

    [Fact]
    public void Empty_epic_with_board_and_lanes()
    {
        var e = New();
        e.SetBoardFilters(new EpicBoardFilters { Lanes = ["Squad Sustain", "General"] });

        var text = EpicApplication.WelcomeText(e, new EpicCounts(), 0);

        Assert.StartsWith($"Olá! Sou o especialista do épico #{e.Number} — Monitorar Triage Sustain.", text);
        Assert.Contains("O épico está ativo, mas ainda vazio: 0 cards, 0 itens de trabalho e 0 histórias.", text);
        Assert.Contains("O board preferido é Product Development Team / Boards, com filtro salvo nas lanes Squad Sustain e General.", text);
        Assert.Contains("* fazer a triagem em lote dos cards novos, com sugestão de área, prioridade e tags;", text);
        Assert.Contains("quando houver cards no épico.", text);
        Assert.EndsWith("Se for o board do time, posso começar por ele.", text);
    }

    [Fact]
    public void Board_without_filters()
    {
        var text = EpicApplication.WelcomeText(New(), new EpicCounts(), 0);
        Assert.Contains("O board preferido é Product Development Team / Boards.", text);
    }

    [Fact]
    public void Without_board_asks_for_the_team()
    {
        var text = EpicApplication.WelcomeText(New(team: null, board: null), new EpicCounts(), 0);
        Assert.Contains("Ainda não há board preferido — me diga o time do DevOps (ou defina no épico).", text);
        Assert.EndsWith("começo definindo o board do time do épico.", text);
    }

    [Fact]
    public void With_content_counts_in_singular_and_plural()
    {
        var text = EpicApplication.WelcomeText(New(), new EpicCounts { Cards = 1, Items = 3 }, 2);
        Assert.Contains("O épico está ativo, com 1 card, 3 itens de trabalho e 2 histórias.", text);
        Assert.Contains("* acompanhar as entregas (PRs e ambientes) dos cards do épico.", text);
    }
}
