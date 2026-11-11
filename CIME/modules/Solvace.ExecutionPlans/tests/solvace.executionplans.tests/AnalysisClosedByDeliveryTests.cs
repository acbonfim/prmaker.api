using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 78262: o plano de correção nasceu e a análise ficou com "propor-solucoes" em andamento — o item #744 nunca
/// concluía. Criado o plano de entrega, a análise fecha: a proposta conta como concluída (não "não executada") e o plano
/// termina concluído.
/// </summary>
public class AnalysisClosedByDeliveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T12:00:00Z");

    private static ExecutionStepDefinition Def(string key) => new() { Key = key, Title = key, Executor = ExecutionExecutor.Claude };

    private static ExecutionPlan Analysis(string proposalStatus)
    {
        var plan = new ExecutionPlan("78262", "analisar-bug", "Análise do bug 78262", null, Guid.NewGuid(), "Alex", Now);
        plan.UpsertSteps([Def("publicar"), Def(AnalysisDelivery.ProposeSolutionsStep)], Now);
        plan.CompleteStep("publicar", "ok", "claude", Now, true);
        plan.UpdateStep(AnalysisDelivery.ProposeSolutionsStep, proposalStatus, "Aguardando", null, null, null, null, "claude", Now,
            proposalStatus == ExecutionStatus.Waiting ? ExecutionWaitingOn.User : null);
        return plan;
    }

    [Theory]
    [InlineData(ExecutionStatus.Running)]
    [InlineData(ExecutionStatus.Waiting)]
    public void Delivery_plan_closes_the_analysis_with_the_proposal_completed(string proposalStatus)
    {
        var plan = Analysis(proposalStatus);

        plan.CompleteStep(AnalysisDelivery.ProposeSolutionsStep, "Solução decidida: plano de correção criado", "Alex", Now, false);
        plan.ChangeStatus(ExecutionStatus.Completed, "Solução decidida: plano de correção criado", null, "Alex", false, Now);

        Assert.Equal(ExecutionStatus.Completed, plan.Status);
        Assert.True(AnalysisDelivery.ProposalCompleted(plan));
        Assert.All(plan.Steps, s => Assert.Equal(ExecutionStatus.Completed, s.Status));
    }
}
