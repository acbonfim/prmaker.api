namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Card 78680: dependências que o tipo da sub-tarefa já diz, no item avulso sem dependência informada — o PR precisa do
/// código/branch do mesmo repositório, o chamado do script, a revisão do PR... Não depende da ordem atual (senão bastaria
/// mover para "soltar" a dependência).
/// </summary>
public static class WorkTaskDependencies
{
    private static readonly IReadOnlyDictionary<string, string[]> Needs = new Dictionary<string, string[]>
    {
        [WorkTaskKind.Code] = [WorkTaskKind.Branch],
        [WorkTaskKind.CherryPick] = [WorkTaskKind.Branch],
        [WorkTaskKind.PullRequest] = [WorkTaskKind.Code, WorkTaskKind.CherryPick, WorkTaskKind.Branch],
        [WorkTaskKind.PrText] = [WorkTaskKind.Code, WorkTaskKind.CherryPick],
        [WorkTaskKind.PrReview] = [WorkTaskKind.PullRequest],
        [WorkTaskKind.Ticket] = [WorkTaskKind.SqlScript],
        [WorkTaskKind.EnvValidation] = [WorkTaskKind.PullRequest],
        [WorkTaskKind.EvidenceComment] = [WorkTaskKind.EnvValidation, WorkTaskKind.EnvReproduction, WorkTaskKind.Validation, WorkTaskKind.Manual],
    };

    /// <summary>Sub-tarefas do item de que <paramref name="task"/> depende pelo tipo (mesmo repositório/destino quando os dois informam).</summary>
    public static IEnumerable<WorkItemTask> Implicit(IEnumerable<WorkItemTask> tasks, WorkItemTask task)
    {
        if (!Needs.TryGetValue(task.Kind, out var kinds)) yield break;
        var p = task.Params;
        foreach (var other in tasks)
        {
            if (ReferenceEquals(other, task) || !kinds.Contains(other.Kind)) continue;
            var o = other.Params;
            if (Differ(p, o, "repository") || Differ(p, o, "target")) continue;
            yield return other;
        }
    }

    private static bool Differ(Dictionary<string, string> a, Dictionary<string, string> b, string key) =>
        a.TryGetValue(key, out var x) && b.TryGetValue(key, out var y) && x.Trim().Length > 0 && y.Trim().Length > 0
        && !string.Equals(x.Trim(), y.Trim(), StringComparison.OrdinalIgnoreCase);
}
