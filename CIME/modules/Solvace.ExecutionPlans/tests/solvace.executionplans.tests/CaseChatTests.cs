using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0080: Especialista do caso — quem ele responde, propostas, ações e o portão da divergência.</summary>
public class CaseChatTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-04T20:00:00Z");

    private static CaseMessage Person(string text, bool peopleOnly = false, string kind = CaseMessageKind.Text)
    {
        var m = new CaseMessage("77881", 1, CaseAuthorKind.Person, Guid.NewGuid(), "Bruno Ferreira", kind, text, kind == CaseMessageKind.Voice, Now);
        m.SetFlags(peopleOnly, false);
        return m;
    }

    [Fact]
    public void Alone_the_specialist_always_answers() => Assert.True(Person("a branch de QA nao aceita PNG").AsksSpecialist(1));

    [Fact]
    public void With_more_people_only_mention_or_question()
    {
        Assert.False(Person("vou testar na QA agora").AsksSpecialist(2));
        Assert.True(Person("o 18 está com limite menor?").AsksSpecialist(2));
        Assert.True(Person("@Especialista confere o parâmetro 18").AsksSpecialist(2));
    }

    [Fact]
    public void People_only_is_never_answered() => Assert.False(Person("@Especialista ignora", peopleOnly: true).AsksSpecialist(1));

    [Fact]
    public void Voice_question_uses_the_transcript()
    {
        var m = Person(string.Empty, kind: CaseMessageKind.Voice);
        m.SetVoice("então não mexe em extensão nenhuma, certo?");
        Assert.True(m.AsksSpecialist(2));
    }

    [Fact]
    public void Mentions_ignore_emails() =>
        Assert.Equal(["Daniel", "Especialista"], CaseMessage.ExtractMentions("@Daniel olha isso, manda pra bruno@solvace.com e @Especialista"));

    [Fact]
    public void Specialist_message_is_not_editable_by_people()
    {
        var m = new CaseMessage("77881", 2, CaseAuthorKind.Specialist, Guid.NewGuid(), "Especialista do caso", CaseMessageKind.Text, "ok", false, Now);
        Assert.False(m.CanBeChangedBy(m.AuthorUserId));
    }

    [Fact]
    public void Empty_message_needs_attachment()
    {
        Assert.Throws<DomainException>(() => Person("  "));
        var ok = new CaseMessage("77881", 3, CaseAuthorKind.Person, Guid.NewGuid(), "Bruno", CaseMessageKind.Text, "", true, Now);
        Assert.Equal(string.Empty, ok.Text);
    }

    private static CaseProposal Proposal(bool execute = false)
    {
        var p = new CaseProposal("77881", 1, "Correção v2 a partir da qa", null, null,
        [
            new CaseProposalChange { Kind = "change", Title = "Corrigir EnvironmentService", Detail = "sem mexer em extensões", FromMessage = 4 },
            new CaseProposalChange { Kind = "add", Title = "Ajustar o parâmetro 18 na QA (você)" }
        ], Now);
        if (execute) p.Accept(true, "Bruno", Now);
        return p;
    }

    [Fact]
    public void Proposal_instruction_tells_the_main_skill_what_to_do()
    {
        var p = Proposal();
        p.Accept(false, "Bruno Ferreira", Now);
        var text = p.InstructionText("Bruno Ferreira");
        Assert.Contains("Proposta do Especialista aceita", text);
        Assert.Contains("prmake_steps", text);
        Assert.DoesNotContain("prmake_correction", text);
        Assert.Contains("(mensagem 4 da conversa)", text);
        Assert.Contains("NÃO execute", text);
        Assert.Contains("Pode executar", Proposal(execute: true).InstructionText("Bruno"));
    }

    [Fact]
    public void Accepted_proposal_adjusts_the_same_plan_without_a_new_version()
    {
        // Card 78344: proposta aceita virava v2 do plano (item igual, comentário duplicado) — o plano não tem versões.
        var correction = Proposal().InstructionText("Alex");
        Assert.Contains("ATUAL, no lugar", correction);
        Assert.DoesNotContain("NOVA VERSÃO", correction);
        var analysis = new CaseProposal("78344", 2, "Outra causa", null, ExecutionPhase.Analysis,
            [new() { Kind = "change", Title = "causa" }], Now).InstructionText("Alex");
        Assert.Contains("sem versão nova", analysis);
    }

    [Fact]
    public void Proposal_is_decided_once()
    {
        var p = Proposal();
        p.Reject("ainda não", "Bruno", Now);
        Assert.Throws<DomainException>(() => p.Accept(false, "Bruno", Now));
    }

    [Fact]
    public void Proposal_needs_changes_and_valid_kinds()
    {
        Assert.Throws<DomainException>(() => new CaseProposal("77881", 1, "t", null, null, [], Now));
        Assert.Throws<DomainException>(() => new CaseProposal("77881", 1, "t", null, null, [new CaseProposalChange { Kind = "merge", Title = "x" }], Now));
    }

    [Fact]
    public void Action_runs_only_after_confirmation()
    {
        var a = new CaseAction("77881", 1, "Subir PNG de 250 KB no upload-image da QA", null, Now);
        Assert.Throws<DomainException>(() => a.Finish(true, "ok", Now));
        a.Decide(true, "Bruno", Now);
        a.Finish(true, "200 OK", Now);
        Assert.Equal(CaseActionStatus.Done, a.Status);
    }

    [Fact]
    public void Pending_divergence_holds_the_pull_request_step()
    {
        var plan = new ExecutionPlan("77881", "analisar-bug", "Correção 77881", null, Guid.NewGuid(), "Bruno", Now, ExecutionPhase.Correction, Guid.NewGuid());
        plan.UpsertSteps([
            new ExecutionStepDefinition { Key = "corrigir", Title = "Corrigir", Kind = ExecutionStepKind.Code },
            new ExecutionStepDefinition { Key = "pr", Title = "PR para QA", Kind = ExecutionStepKind.PullRequest }
        ], Now);
        plan.RaiseDivergence("A qa já tem o fix do 75706; a análise foi feita na master.", "corrigir", Now);
        Assert.True(plan.DivergencePending);
        plan.UpdateStep("corrigir", ExecutionStatus.Running, null, null, null, null, null, "Claude", Now);
        Assert.Throws<DomainException>(() => plan.UpdateStep("pr", ExecutionStatus.Running, null, null, null, null, null, "Claude", Now));
        plan.ConfirmDivergence("Bruno", Now);
        plan.UpdateStep("pr", ExecutionStatus.Running, null, null, null, null, null, "Claude", Now);
        Assert.False(plan.DivergencePending);
    }

    [Fact]
    public void Specialist_lane_is_separate() =>
        Assert.Equal((ExecutionLane.Specialist, ExecutionLane.Main), (ExecutionLane.Of(ExecutionRequestKind.Specialist), ExecutionLane.Of(ExecutionRequestKind.Resume)));
}
