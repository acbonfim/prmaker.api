using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 78728: com o PR aguardando o merge, o tratamento mandou <c>prmake_steps</c> só com <c>preparar-fechamento</c>
/// (para trocar o dependsOn) e o PRMake apagou <c>validar-qa</c> e <c>fechar-card</c> por omissão — no merge o plano
/// concluiu sem a validação em QA. Lista parcial agora só muda as etapas enviadas.
/// </summary>
public class PartialStepsUpsertTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-23T13:04:00Z");

    private static ExecutionStepDefinition Def(string key, string? executor = null, params string[] dependsOn) =>
        new() { Key = key, Title = key, Executor = executor ?? ExecutionExecutor.Claude, DependsOn = dependsOn.Length > 0 ? [.. dependsOn] : null };

    private static ExecutionPlan Plan78728()
    {
        var plan = new ExecutionPlan("78728", "analisar-bug", "Correção 78728", null, Guid.NewGuid(), "Alex", Now);
        plan.UpsertSteps([
            Def("impacto-na-base"),
            Def("corrigir-edv-solvace", null, "impacto-na-base"),
            Def("validar", null, "corrigir-edv-solvace"),
            Def("pr-edv-solvace", null, "validar"),
            Def("preparar-fechamento", null, "pr-edv-solvace"),
            Def("validar-qa", ExecutionExecutor.User, "pr-edv-solvace", "preparar-fechamento"),
            Def("fechar-card", null, "validar-qa"),
        ], Now);
        foreach (var key in new[] { "impacto-na-base", "corrigir-edv-solvace", "validar" })
            plan.CompleteStep(key, "ok", "claude", Now, true);
        return plan;
    }

    private static string[] Keys(ExecutionPlan plan) => plan.Steps.OrderBy(s => s.Order).Select(s => s.Key).ToArray();

    [Fact]
    public void Partial_list_changes_only_the_sent_step_and_keeps_the_others_in_place()
    {
        var plan = Plan78728();
        var before = Keys(plan);

        plan.UpsertSteps([new ExecutionStepDefinition { Key = "preparar-fechamento", DependsOn = ["validar"] }], Now, replace: false);

        Assert.Equal(before, Keys(plan));
        var step = plan.Steps.Single(s => s.Key == "preparar-fechamento");
        Assert.Equal("preparar-fechamento", step.Title);
        Assert.Equal(["validar"], step.DependsOn);
    }

    [Fact]
    public void Partial_list_adds_a_new_step_after_the_previous_sent_one()
    {
        var plan = Plan78728();

        plan.UpsertSteps([Def("pr-edv-solvace", null, "validar"), Def("revisar-pr", null, "pr-edv-solvace")], Now, replace: false);

        Assert.Equal(["impacto-na-base", "corrigir-edv-solvace", "validar", "pr-edv-solvace", "revisar-pr", "preparar-fechamento",
            "validar-qa", "fechar-card"], Keys(plan));
    }

    [Fact]
    public void Replace_still_drops_the_omitted_pending_steps()
    {
        var plan = Plan78728();

        plan.UpsertSteps([Def("impacto-na-base"), Def("corrigir-edv-solvace"), Def("validar"), Def("pr-edv-solvace"), Def("fechar-card")],
            Now, replace: true);

        Assert.Equal(["impacto-na-base", "corrigir-edv-solvace", "validar", "pr-edv-solvace", "fechar-card"], Keys(plan));
    }

    [Fact]
    public void Full_list_without_replace_reorders_as_before()
    {
        var plan = Plan78728();

        plan.UpsertSteps([Def("impacto-na-base"), Def("corrigir-edv-solvace"), Def("validar"), Def("pr-edv-solvace"),
            Def("validar-qa", ExecutionExecutor.User), Def("preparar-fechamento"), Def("fechar-card")], Now, replace: false);

        Assert.Equal(["impacto-na-base", "corrigir-edv-solvace", "validar", "pr-edv-solvace", "validar-qa", "preparar-fechamento",
            "fechar-card"], Keys(plan));
    }
}
