using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// 0096 (card 78344): ajuste pedido ao especialista virava versão nova do plano — item igual ao lado (#803 × #800) e
/// comentário novo na discussion. Agora o plano é ajustado no lugar (a sub-tarefa pendente acompanha o texto da etapa) e a
/// versão nova, quando a solução muda, reaproveita o item da anterior.
/// </summary>
public class SpecialistGuardian0096Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-13T13:00:00Z");

    private static WorkItem Correction(Guid planId) =>
        new("78344", WorkItemType.Fix, "Correcao 78344: remover os 3 posts", null, WorkItemOrigin.Correction, null, planId,
        [
            new WorkTaskInput { Title = "Excluir os 3 posts", Kind = WorkTaskKind.Manual, Executor = ExecutionExecutor.User, StepKey = "remover-posts" },
            new WorkTaskInput { Title = "Fechamento", Description = "resumo PT/EN", Kind = WorkTaskKind.Manual, StepKey = "fechar-card" }
        ], Guid.NewGuid(), "Alex", Now);

    [Fact]
    public void Pending_task_follows_the_step_text()
    {
        var item = Correction(Guid.NewGuid());

        Assert.True(item.SyncStepText("fechar-card", "Fechamento", "resumo PT/EN em primeira pessoa", Now));

        Assert.Equal("resumo PT/EN em primeira pessoa", item.OrderedTasks.Single(t => t.StepKey == "fechar-card").Description);
        Assert.False(item.SyncStepText("fechar-card", "Fechamento", "resumo PT/EN em primeira pessoa", Now));
    }

    [Fact]
    public void Started_task_keeps_its_text()
    {
        var item = Correction(Guid.NewGuid());
        item.SyncStep("remover-posts", ExecutionStatus.Running, null, "Plano", Now);

        Assert.False(item.SyncStepText("remover-posts", "Outro título", null, Now));
        Assert.Equal("Excluir os 3 posts", item.OrderedTasks.First().Title);
    }

    [Fact]
    public void Waiting_task_of_the_person_follows_the_new_instruction()
    {
        var item = Correction(Guid.NewGuid());
        item.SyncStep("remover-posts", ExecutionStatus.Waiting, null, "Plano", Now);

        Assert.True(item.SyncStepText("remover-posts", "Excluir os 3 posts", "Eu excluo com o adminlkr", Now));
        Assert.Equal("Eu excluo com o adminlkr", item.OrderedTasks.First().Description);
    }

    [Fact]
    public void New_version_adopts_the_open_item_and_skips_steps_that_left()
    {
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var item = Correction(v1);
        item.SyncStep("remover-posts", ExecutionStatus.Waiting, null, "Plano", Now);

        item.AdoptPlan(v2, new HashSet<string> { "remover-posts", "fechar-card-primeira-pessoa" }, "v2", "Alex", Now);

        Assert.Equal(v2, item.PlanId);
        Assert.Equal(WorkTaskStatus.Waiting, item.OrderedTasks.Single(t => t.StepKey == "remover-posts").Status);
        Assert.Equal(WorkTaskStatus.Skipped, item.OrderedTasks.Single(t => t.StepKey == "fechar-card").Status);
        Assert.True(item.MirrorsPlan);
    }

    [Fact]
    public void Closed_item_does_not_follow_the_new_version()
    {
        var item = Correction(Guid.NewGuid());
        item.Cancel("cancelado", "Alex", Now);

        Assert.Throws<DomainException>(() => item.AdoptPlan(Guid.NewGuid(), new HashSet<string> { "remover-posts" }, "v2", "Alex", Now));
    }

    [Theory]
    [InlineData("Substituída pela versão nova do plano de correção (v2)", true)]
    [InlineData("Cancelado pela tela", false)]
    [InlineData(null, false)]
    public void Superseded_plan_is_told_apart_from_a_cancelled_one(string? reason, bool superseded) =>
        Assert.Equal(superseded, PlanTerms.IsSuperseded(reason));
}
