using solvace.executionplans.application;
using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0107: escolha clicável na mensagem do especialista — botões, escolha única, checklist e lista suspensa.</summary>
public class CaseChoice0107Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-27T18:00:00Z");

    private static CaseChoiceOption O(string label, string? id = null, string? style = null) => new() { Id = id ?? string.Empty, Label = label, Style = style ?? string.Empty };

    private static CaseChoice Buttons() => CaseChoice.Create("buttons", null,
        [O("Criar item e executar agora", "executar", "primary"), O("Só criar o item", "criar"), O("Cancelar", "cancelar", "warn")], false, null);

    private static CaseMessage SpecialistMessage(int number = 5) =>
        new("79047", number, CaseAuthorKind.Specialist, Guid.NewGuid(), "Especialista do caso", CaseMessageKind.Text, "Posso seguir?", false, Now);

    [Fact]
    public void Buttons_keep_ids_and_styles()
    {
        var c = Buttons();
        Assert.Equal(CaseChoiceMode.Buttons, c.Mode);
        Assert.Equal(["executar", "criar", "cancelar"], c.Options.Select(o => o.Id));
        Assert.Equal([CaseChoiceStyle.Primary, CaseChoiceStyle.Neutral, CaseChoiceStyle.Danger], c.Options.Select(o => o.Style));
        Assert.True(c.IsOpen);
    }

    [Fact]
    public void Missing_or_repeated_ids_are_numbered()
    {
        var c = CaseChoice.Create("single", "Qual termo?", [O("Termo A"), O("Termo B", "x"), O("Termo C", "x")], false, null);
        Assert.Equal(["1", "x", "3"], c.Options.Select(o => o.Id));
    }

    [Fact]
    public void Modes_accept_portuguese_aliases_and_reject_unknown()
    {
        Assert.Equal(CaseChoiceMode.Multi, CaseChoiceMode.Normalize("checklist"));
        Assert.Equal(CaseChoiceMode.Select, CaseChoiceMode.Normalize("picklist"));
        Assert.Throws<DomainException>(() => CaseChoiceMode.Normalize("slider"));
    }

    [Fact]
    public void Single_needs_two_options_and_buttons_at_most_six()
    {
        Assert.Throws<DomainException>(() => CaseChoice.Create("single", null, [O("Só uma")], false, null));
        Assert.NotNull(CaseChoice.Create("single", null, [O("Só uma")], true, null));
        Assert.Throws<DomainException>(() => CaseChoice.Create("buttons", null, Enumerable.Range(1, 7).Select(i => O($"B{i}")), false, null));
    }

    [Fact]
    public void One_answer_only()
    {
        var c = Buttons();
        var picked = c.Respond(["executar"], null, Guid.NewGuid(), "Alex Carlos", Now);
        Assert.Equal("Criar item e executar agora", Assert.Single(picked).Label);
        Assert.False(c.IsOpen);
        var e = Assert.Throws<DomainException>(() => c.Respond(["cancelar"], null, Guid.NewGuid(), "Rui", Now));
        Assert.Contains("Alex Carlos", e.Message);
    }

    [Fact]
    public void Single_rejects_two_and_unknown_options()
    {
        var c = CaseChoice.Create("single", null, [O("A", "a"), O("B", "b")], false, null);
        Assert.Throws<DomainException>(() => c.Respond(["a", "b"], null, null, "Alex", Now));
        Assert.Throws<DomainException>(() => c.Respond(["z"], null, null, "Alex", Now));
        Assert.Throws<DomainException>(() => c.Respond([], null, null, "Alex", Now));
        Assert.True(c.IsOpen);
    }

    [Fact]
    public void Checklist_takes_several_and_other_text_only_when_allowed()
    {
        var multi = CaseChoice.Create("multi", "Quais cenários?", [O("Sala removida", "1"), O("Outra planta", "2"), O("Link antigo", "3")], true, "Outro cenário");
        var picked = multi.Respond(["1", "3"], "Usuário em duas plantas", null, "Alex", Now);
        Assert.Equal(2, picked.Count);
        Assert.Equal("- **Sala removida**\n- **Link antigo**\n- Outra: Usuário em duas plantas", CaseChoice.AnswerText(picked, multi.Answer!.Other));

        var noOther = CaseChoice.Create("single", null, [O("A"), O("B")], false, null);
        Assert.Throws<DomainException>(() => noOther.Respond([], "texto livre", null, "Alex", Now));
    }

    [Fact]
    public void Closed_choice_cannot_be_answered()
    {
        var c = Buttons();
        c.Close(Now);
        Assert.False(c.IsOpen);
        Assert.Throws<DomainException>(() => c.Respond(["executar"], null, null, "Alex", Now));
    }

    [Fact]
    public void Message_keeps_the_choice_and_the_answer_as_json()
    {
        var m = SpecialistMessage();
        var c = Buttons();
        m.SetChoice(c);
        var read = m.Choice!;
        read.Respond(["criar"], null, null, "Alex", Now);
        read.Answer!.MessageNumber = 6;
        m.SetChoice(read);
        Assert.Equal(["criar"], m.Choice!.Answer!.Selected);
        Assert.Equal(6, m.Choice.Answer.MessageNumber);
    }

    [Fact]
    public void Only_the_specialist_sends_choices()
    {
        var person = new CaseMessage("79047", 7, CaseAuthorKind.Person, Guid.NewGuid(), "Alex", CaseMessageKind.Text, "oi", false, Now);
        Assert.Throws<DomainException>(() => person.SetChoice(Buttons()));
    }

    [Fact]
    public void Context_line_shows_options_and_state()
    {
        var m = SpecialistMessage();
        var c = Buttons();
        m.SetChoice(c);
        Assert.Contains("[escolha buttons: executar=Criar item e executar agora | criar=Só criar o item | cancelar=Cancelar — aguardando o clique]",
            CaseChatApplication.MessageLine(m, []));
        c.Respond(["executar"], null, null, "Alex Carlos", Now);
        m.SetChoice(c);
        Assert.Contains("respondida por Alex Carlos: Criar item e executar agora", CaseChatApplication.MessageLine(m, []));
    }
}
