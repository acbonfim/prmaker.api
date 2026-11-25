using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0100: todo item de trabalho que muda código ganha a sub-tarefa <c>aws-deploy-map</c> (mapear as esteiras do card por
/// ambiente e se o nosso código já está em cada um — somente leitura, pela CLI da AWS da máquina). Uma por item, logo depois
/// da última sub-tarefa de código (<c>code</c>, <c>cherry-pick</c>, <c>pr</c>) — portanto antes da validação/QA/estado do
/// card que vêm depois dela. Idempotente: não repete e não volta se a pessoa tirou (<see cref="WorkItem.AwsDeployMapApplied"/>
/// no item avulso; no plano, a etapa fica — cancelada/pulada — e conta como existente).
/// <list type="bullet">
/// <item>Item avulso (tela, modelo, especialista): <see cref="Apply"/> nas sub-tarefas ao criar, editar e acrescentar.</item>
/// <item>Item espelho do plano de correção: a etapa <see cref="StepKey"/> entra no plano (<see cref="EnsurePlanStep"/>) e o item
/// ganha a sub-tarefa pela sincronização — o tratamento a executa como as outras etapas.</item>
/// </list>
/// </summary>
public static class WorkItemAwsDeployRule
{
    public const string Title = "Mapear deploy na AWS (ambientes e esteiras)";
    public const string StepKey = "mapear-deploy-aws";

    public const string Instruction =
        "Rode a coleta do aws-deploy para o card (bash ~/.claude/skills/aws-deploy/scripts/aws.sh collect <card> — somente leitura, " +
        "pela CLI da AWS desta máquina; grava o retrato na aba AWS do PRMake) e conclua com o resumo por ambiente: se o nosso código " +
        "já está em DEV, QA, Sandboxefeso, SandboxRT e Produção, e as esteiras (pipelines) de cada um com o status e o commit implantado " +
        "(prmake_aws_status). Sem a CLI da AWS ou sem permissão: conclua dizendo o que faltou, sem travar o item.";

    /// <summary>Tipos de sub-tarefa que mudam código (o deploy depende deles).</summary>
    public static readonly IReadOnlySet<string> CodeKinds = new HashSet<string>
        { WorkTaskKind.Code, WorkTaskKind.CherryPick, WorkTaskKind.PullRequest };

    public static WorkTaskInput NewTask() => new()
    {
        Title = Title,
        Kind = WorkTaskKind.AwsDeployMap,
        Executor = ExecutionExecutor.Claude,
        Description = Instruction,
    };

    /// <summary>
    /// Sub-tarefas com a <c>aws-deploy-map</c> logo depois da última de código (a lista original quando não se aplica: sem
    /// código, já com ela, já aplicada antes no item, espelho do plano ou sem espaço até <see cref="WorkItem.MaxTasks"/>).
    /// </summary>
    /// <param name="alreadyApplied">O item já teve a sub-tarefa (a pessoa tirou → não volta).</param>
    public static IReadOnlyList<WorkTaskInput> Apply(IReadOnlyList<WorkTaskInput> tasks, bool alreadyApplied = false)
    {
        if (alreadyApplied || tasks.Count == 0 || tasks.Count + 1 > WorkItem.MaxTasks) return tasks;
        if (tasks.Any(t => Is(t.Kind, WorkTaskKind.AwsDeployMap))) return tasks;
        // Sub-tarefa espelho de etapa: o plano é a fonte (EnsurePlanStep).
        if (tasks.Any(t => !string.IsNullOrWhiteSpace(t.StepKey))) return tasks;
        var last = -1;
        for (var i = 0; i < tasks.Count; i++)
            if (IsCode(tasks[i].Kind)) last = i;
        if (last < 0) return tasks;
        var result = tasks.ToList();
        result.Insert(last + 1, NewTask());
        return result;
    }

    /// <summary>
    /// Sub-tarefas a acrescentar num item avulso aberto: se o item ainda não teve a <c>aws-deploy-map</c> e as novas mudam
    /// código, ela vai junto, logo depois da última nova de código.
    /// </summary>
    public static IReadOnlyList<WorkTaskInput> ApplyToAdded(WorkItem item, IReadOnlyList<WorkTaskInput> added)
    {
        if (WorkItemStatus.IsClosed(item.Status) || item.Status is WorkItemStatus.Reverting) return added;
        if (item.AwsDeployMapApplied || item.Tasks.Any(t => Is(t.Kind, WorkTaskKind.AwsDeployMap))) return added;
        if (item.Tasks.Count + added.Count + 1 > WorkItem.MaxTasks) return added;
        return Apply(added);
    }

    /// <summary>
    /// Plano de correção com etapa de código/PR e sem a etapa da AWS (em qualquer status): acrescenta <see cref="StepKey"/>
    /// logo depois da última etapa de código, antes da primeira etapa ainda não terminada que vem depois dela (ou no fim). A
    /// etapa fica fixa no lugar (<c>OrderPinned</c>): a skill que reenvia as etapas não a remove. Devolve se acrescentou.
    /// </summary>
    public static bool EnsurePlanStep(ExecutionPlan plan, DateTimeOffset now)
    {
        if (plan.Phase != ExecutionPhase.Correction || plan.IsFinished) return false;
        var ordered = plan.Steps.OrderBy(s => s.Order).ToList();
        if (ordered.Any(s => s.Key == StepKey || Is(s.TaskKind, WorkTaskKind.AwsDeployMap))) return false;
        var last = ordered.FindLastIndex(s => IsCode(WorkTaskKind.FromStep(s.Kind, s.Executor, s.Key, s.Title, s.TaskKind)));
        if (last < 0) return false;

        var anchor = ordered.Skip(last + 1).FirstOrDefault(s => !ExecutionStatus.IsStepFinished(s.Status));
        var definition = new ExecutionStepDefinition
        {
            Key = StepKey,
            Title = Title,
            Description = Instruction,
            Executor = ExecutionExecutor.Claude,
            Kind = ExecutionStepKind.Task,
            TaskKind = WorkTaskKind.AwsDeployMap,
        };
        try
        {
            plan.AddStep(definition, anchor?.Key, now, anchor is null ? TaskPosition.Last : null);
            return true;
        }
        catch (DomainException)
        {
            // Dependências não deixam a etapa nessa posição (ou o plano mudou de estado): fica sem ela.
            return false;
        }
    }

    public static bool IsCode(string? kind) => kind is not null && CodeKinds.Contains(kind.Trim().ToLowerInvariant());

    private static bool Is(string? kind, string expected) => string.Equals(kind?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
