using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// 0100: sub-tarefa automática <c>aws-deploy-map</c> — uma por item que muda código, logo depois da última de código e antes
/// da validação/estado do card; não volta se a pessoa tirou; no plano de correção vira etapa fixa no lugar.
/// </summary>
public class WorkItemAwsDeployRuleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T19:00:00Z");

    private static WorkTaskInput T(string kind, string? title = null) => new() { Kind = kind, Title = title ?? kind };

    private static string[] Kinds(IEnumerable<WorkTaskInput> tasks) => tasks.Select(t => t.Kind!).ToArray();

    [Fact]
    public void Goes_after_the_last_code_task_and_before_validation()
    {
        var result = WorkItemAwsDeployRule.Apply([T("branch"), T("code"), T("pr"), T("devops-state"), T("env-validation")]);
        Assert.Equal(["branch", "code", "pr", "aws-deploy-map", "devops-state", "env-validation"], Kinds(result));
        var aws = result[3];
        Assert.Equal(WorkItemAwsDeployRule.Title, aws.Title);
        Assert.Equal(ExecutionExecutor.Claude, aws.Executor);
        Assert.Contains("aws.sh collect", aws.Description);
    }

    [Fact]
    public void Cherry_pick_counts_as_code()
    {
        var result = WorkItemAwsDeployRule.Apply([T("cherry-pick"), T("validation")]);
        Assert.Equal(["cherry-pick", "aws-deploy-map", "validation"], Kinds(result));
    }

    [Fact]
    public void Exactly_one_and_idempotent()
    {
        var once = WorkItemAwsDeployRule.Apply([T("code"), T("pr")]);
        var twice = WorkItemAwsDeployRule.Apply(once);
        Assert.Same(once, twice);
        Assert.Single(twice, t => t.Kind == WorkTaskKind.AwsDeployMap);
    }

    [Fact]
    public void Items_without_code_or_mirroring_the_plan_are_left_alone()
    {
        IReadOnlyList<WorkTaskInput> noCode = [T("comment"), T("validation")];
        Assert.Same(noCode, WorkItemAwsDeployRule.Apply(noCode));
        Assert.Empty(WorkItemAwsDeployRule.Apply([]));
        IReadOnlyList<WorkTaskInput> mirror = [new() { Kind = "code", Title = "Corrigir", StepKey = "corrigir" }];
        Assert.Same(mirror, WorkItemAwsDeployRule.Apply(mirror));
    }

    [Fact]
    public void Full_item_gets_nothing()
    {
        IReadOnlyList<WorkTaskInput> full = Enumerable.Range(0, WorkItem.MaxTasks).Select(_ => T("code")).ToList();
        Assert.Same(full, WorkItemAwsDeployRule.Apply(full));
    }

    [Fact]
    public void Removed_by_the_person_does_not_come_back()
    {
        var tasks = WorkItemAwsDeployRule.Apply([T("code"), T("pr")]);
        var item = new WorkItem("78720", "fix", "Corrigir", null, WorkItemOrigin.User, null, null, tasks, Guid.NewGuid(), "Alex", Now);
        Assert.True(item.AwsDeployMapApplied);

        // A pessoa edita o item e tira a sub-tarefa da AWS: a regra respeita.
        var edited = WorkItemAwsDeployRule.Apply([T("code"), T("pr")], item.AwsDeployMapApplied);
        item.Update("Corrigir", null, edited, "Alex", Now);
        Assert.DoesNotContain(item.Tasks, t => t.Kind == WorkTaskKind.AwsDeployMap);
        Assert.True(item.AwsDeployMapApplied);
        Assert.Same(edited, WorkItemAwsDeployRule.Apply(edited, item.AwsDeployMapApplied));
    }

    [Fact]
    public void Added_code_tasks_bring_it_along_once()
    {
        var item = new WorkItem("78720", "task", "Revisar", null, WorkItemOrigin.User, null, null, [T("comment")], Guid.NewGuid(), "Alex", Now);
        Assert.False(item.AwsDeployMapApplied);
        var added = WorkItemAwsDeployRule.ApplyToAdded(item, [T("code"), T("pr")]);
        Assert.Equal(["code", "pr", "aws-deploy-map"], Kinds(added));
        item.AddTasks(added, null, "Alex", Now);
        Assert.True(item.AwsDeployMapApplied);

        IReadOnlyList<WorkTaskInput> more = [T("code")];
        Assert.Same(more, WorkItemAwsDeployRule.ApplyToAdded(item, more));
    }

    [Fact]
    public void Closed_items_get_nothing()
    {
        var item = new WorkItem("78720", "task", "Revisar", null, WorkItemOrigin.User, null, null, [T("comment")], Guid.NewGuid(), "Alex", Now);
        item.Cancel("não precisa", "Alex", Now);
        IReadOnlyList<WorkTaskInput> added = [T("code")];
        Assert.Same(added, WorkItemAwsDeployRule.ApplyToAdded(item, added));
    }

    // ---------------------------------------------------------------- plano de correção

    private static ExecutionStepDefinition Def(string key, string kind = ExecutionStepKind.Task, string? taskKind = null) =>
        new() { Key = key, Title = key, Kind = kind, TaskKind = taskKind };

    private static ExecutionPlan Correction(params ExecutionStepDefinition[] steps)
    {
        var plan = new ExecutionPlan("78720", "analisar-bug", "Correção 78720", null, Guid.NewGuid(), "Alex", Now,
            ExecutionPhase.Correction, Guid.NewGuid());
        plan.UpsertSteps(steps, Now);
        return plan;
    }

    private static string[] Keys(ExecutionPlan plan) => plan.Steps.OrderBy(s => s.Order).Select(s => s.Key).ToArray();

    [Fact]
    public void Correction_plan_gets_the_step_after_the_last_code_step()
    {
        var plan = Correction(Def("corrigir-codigo", ExecutionStepKind.Code), Def("abrir-pr-qa", ExecutionStepKind.PullRequest),
            Def("validar-qa", ExecutionStepKind.Validation), Def("fechar-card"));
        Assert.True(WorkItemAwsDeployRule.EnsurePlanStep(plan, Now));
        Assert.Equal(["corrigir-codigo", "abrir-pr-qa", "mapear-deploy-aws", "validar-qa", "fechar-card"], Keys(plan));
        var step = plan.Steps.Single(s => s.Key == WorkItemAwsDeployRule.StepKey);
        Assert.Equal(WorkTaskKind.AwsDeployMap, step.TaskKind);
        Assert.True(step.OrderPinned);

        // Idempotente, e a skill reenviando as etapas não a remove nem a move.
        Assert.False(WorkItemAwsDeployRule.EnsurePlanStep(plan, Now));
        plan.UpsertSteps([Def("corrigir-codigo", ExecutionStepKind.Code), Def("abrir-pr-qa", ExecutionStepKind.PullRequest),
            Def("validar-qa", ExecutionStepKind.Validation), Def("fechar-card")], Now);
        Assert.Equal(["corrigir-codigo", "abrir-pr-qa", "mapear-deploy-aws", "validar-qa", "fechar-card"], Keys(plan));
        Assert.False(WorkItemAwsDeployRule.EnsurePlanStep(plan, Now));
    }

    [Fact]
    public void Cancelled_step_counts_as_existing()
    {
        var plan = Correction(Def("corrigir-codigo", ExecutionStepKind.Code), Def("validar-qa", ExecutionStepKind.Validation));
        WorkItemAwsDeployRule.EnsurePlanStep(plan, Now);
        plan.UpdateStep(WorkItemAwsDeployRule.StepKey, ExecutionStatus.Cancelled, "Sem AWS", null, null, null, null, "Alex", Now, fromExecutor: false);
        Assert.False(WorkItemAwsDeployRule.EnsurePlanStep(plan, Now));
        Assert.Single(plan.Steps, s => s.Key == WorkItemAwsDeployRule.StepKey);
    }

    [Fact]
    public void Plans_without_code_and_analysis_plans_are_left_alone()
    {
        var noCode = Correction(Def("gerar-script", taskKind: "sql-script"), Def("chamado", ExecutionStepKind.Ticket));
        Assert.False(WorkItemAwsDeployRule.EnsurePlanStep(noCode, Now));

        var analysis = new ExecutionPlan("78720", "analisar-bug", "Análise 78720", null, Guid.NewGuid(), "Alex", Now);
        analysis.UpsertSteps([Def("corrigir-codigo", ExecutionStepKind.Code)], Now);
        Assert.False(WorkItemAwsDeployRule.EnsurePlanStep(analysis, Now));
        Assert.DoesNotContain(analysis.Steps, s => s.Key == WorkItemAwsDeployRule.StepKey);
    }

    [Fact]
    public void Last_code_step_at_the_end_puts_it_at_the_end()
    {
        var plan = Correction(Def("entender"), Def("corrigir-codigo", ExecutionStepKind.Code));
        Assert.True(WorkItemAwsDeployRule.EnsurePlanStep(plan, Now));
        Assert.Equal(["entender", "corrigir-codigo", "mapear-deploy-aws"], Keys(plan));
    }
}
