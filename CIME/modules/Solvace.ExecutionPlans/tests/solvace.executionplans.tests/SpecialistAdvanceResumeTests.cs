using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 78505, item #874: o especialista concluiu a etapa da pessoa ("registrar-melhoria") e o fechamento ficou "em
/// andamento" sem ninguém trabalhando — a mudança marcava atividade do executor (a fila achava o Claude vivo) e nenhum
/// executor era pedido. Só o executor conta como atividade; a etapa seguinte fica pronta para o tratamento.
/// </summary>
public class SpecialistAdvanceResumeTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-17T13:25:00Z");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T13:46:10Z");

    private static ExecutionPlan Correction()
    {
        var plan = new ExecutionPlan("78505", "analisar-bug", "Correção 78505", null, Guid.NewGuid(), "Alex", Created);
        plan.UpsertSteps([
            new ExecutionStepDefinition { Key = "registrar-melhoria", Title = "Registrar a melhoria", Executor = ExecutionExecutor.User },
            new ExecutionStepDefinition { Key = "fechar-card", Title = "Fechamento", Executor = ExecutionExecutor.Claude, DependsOn = ["registrar-melhoria"] },
        ], Created);
        plan.UpdateStep("registrar-melhoria", ExecutionStatus.Waiting, "Aguardando você", null, null, null, null, "claude", Created,
            ExecutionWaitingOn.User);
        return plan;
    }

    [Fact]
    public void Specialist_completing_the_person_step_does_not_look_like_the_executor_working()
    {
        var plan = Correction();
        var before = plan.LastActivityAt;

        plan.UpdateStep("registrar-melhoria", ExecutionStatus.Completed, null, null, null, null, null,
            "Especialista do caso, a pedido de Alex", Now, fromExecutor: false);

        Assert.Equal(before, plan.LastActivityAt);
        Assert.Equal(ExecutionStatus.Pending, plan.Steps.Single(s => s.Key == "fechar-card").Status);
        Assert.Contains(plan.ReadySteps(), s => s.Key == "fechar-card");
    }

    [Fact]
    public void Executor_step_change_still_counts_as_activity()
    {
        var plan = Correction();
        plan.UpdateStep("fechar-card", ExecutionStatus.Running, null, null, null, null, null, "Claude", Now);
        Assert.Equal(Now, plan.LastActivityAt);
    }
}
