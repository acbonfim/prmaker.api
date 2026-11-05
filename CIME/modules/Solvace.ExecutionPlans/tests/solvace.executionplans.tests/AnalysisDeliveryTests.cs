using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>Card 77988: "só a análise por enquanto" não pode deixar o card sem caminho para os itens de trabalho.</summary>
public class AnalysisDeliveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-05T12:00:00Z");

    private static ExecutionPlan Analysis(string kind = "analisar-us") =>
        new("77988", kind, "Análise da US 77988", null, Guid.NewGuid(), "Alex", Now);

    private static ExecutionQuestion Q(int order, string? answer)
    {
        var q = new ExecutionQuestion(Guid.NewGuid(), AnalysisDelivery.ProposeSolutionsStep, order, $"Pergunta {order}", null, true, "claude", Now);
        if (answer is not null) q.AnswerWith(answer, "Alex", false, Now);
        return q;
    }

    [Fact]
    public void Answered_analysis_without_delivery_plan_can_ask_for_the_items()
    {
        var plan = Analysis();
        plan.ChangeStatus(ExecutionStatus.Completed, null, "resumo", "Claude", true, Now);

        Assert.Null(AnalysisDelivery.Refusal(plan, [Q(1, "Uma foto por usuário"), Q(6, "Não, só a análise por enquanto")]));
    }

    [Fact]
    public void Open_question_in_the_proposal_blocks_the_request()
    {
        var refusal = AnalysisDelivery.Refusal(Analysis(), [Q(1, "Sim"), Q(2, null)]);
        Assert.Contains("responda as perguntas", refusal);
    }

    [Fact]
    public void Card_without_plan_or_with_delivery_plan_is_refused()
    {
        Assert.Contains("analise primeiro", AnalysisDelivery.Refusal(null, []));
        var delivery = new ExecutionPlan("77988", "analisar-us", "Implementação 77988", null, Guid.NewGuid(), "Alex", Now,
            ExecutionPhase.Correction, Guid.NewGuid());
        Assert.Contains("já tem plano de implementação", AnalysisDelivery.Refusal(delivery, []));
    }

    [Fact]
    public void Instruction_overrides_the_previous_answer_and_pauses_when_not_starting()
    {
        var paused = AnalysisDelivery.Instruction("analisar-us", false, "só o back por enquanto");
        Assert.Contains("plano de implementacao", paused);
        Assert.Contains("so a analise por enquanto", paused);
        Assert.Contains("\"paused\"", paused);
        Assert.EndsWith("só o back por enquanto", paused);

        var now = AnalysisDelivery.Instruction("analisar-bug", true, null);
        Assert.Contains("plano de correcao", now);
        Assert.Contains("siga executando", now);
        Assert.DoesNotContain("paused", now);
    }
}
