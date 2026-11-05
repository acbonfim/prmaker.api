namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0085: status do item de trabalho (gravado como texto). Compartilhado com o frontend (WorkItem) e com as skills
/// (executar/desfazer item) — mantenha em sincronia.
/// </summary>
public static class WorkItemStatus
{
    public const string Pending = "pending";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Waiting = "waiting";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Reverting = "reverting";
    public const string Reverted = "reverted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { Pending, Queued, Running, Waiting, Completed, Failed, Cancelled, Reverting, Reverted };

    /// <summary>Item que não conta na verificação de duplicidade (foi cancelado ou desfeito).</summary>
    public static readonly IReadOnlySet<string> Discarded = new HashSet<string> { Cancelled, Reverted };

    /// <summary>Item fechado: não roda mais (só desfazer, no caso de concluído/cancelado/falho).</summary>
    public static bool IsClosed(string status) => status is Completed or Cancelled or Reverted;
}

/// <summary>0085: status da sub-tarefa do item.</summary>
public static class WorkTaskStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Waiting = "waiting";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Cancelled = "cancelled";
    public const string Reverted = "reverted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { Pending, Running, Waiting, Completed, Failed, Skipped, Cancelled, Reverted };

    /// <summary>Sub-tarefa que conta como feita no progresso.</summary>
    public static bool IsDone(string status) => status is Completed or Skipped;

    /// <summary>Sub-tarefa ainda aberta (cancelar o item cancela estas).</summary>
    public static bool IsOpen(string status) => status is Pending or Running or Waiting;
}

/// <summary>0085: ações sobre a sub-tarefa (REST <c>tasks/{n}/{action}</c> e MCP <c>prmake_work_task</c>).</summary>
public static class WorkTaskAction
{
    public const string Start = "start";
    public const string Complete = "complete";
    public const string Fail = "fail";
    public const string Wait = "wait";
    public const string Skip = "skip";
    public const string Revert = "revert";
    public const string Cancel = "cancel";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Start, Complete, Fail, Wait, Skip, Revert, Cancel };
}

/// <summary>0085: de onde veio o item.</summary>
public static class WorkItemOrigin
{
    public const string User = "user";
    public const string Specialist = "specialist";
    public const string Analysis = "analysis";
    public const string Correction = "correction";
    public const string Template = "template";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { User, Specialist, Analysis, Correction, Template };
}

/// <summary>0085: tipo do item (segue o tipo do card no DevOps).</summary>
public static class WorkItemType
{
    public const string Bug = "bug";
    public const string UserStory = "user-story";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Bug, UserStory };

    /// <summary>Tipo do item pelo <c>System.WorkItemType</c> do card ("User Story" → user-story; o resto → bug).</summary>
    public static string FromCardType(string? workItemType) =>
        string.Equals(workItemType?.Trim(), "User Story", StringComparison.OrdinalIgnoreCase)
        || string.Equals(workItemType?.Trim(), UserStory, StringComparison.OrdinalIgnoreCase)
            ? UserStory : Bug;

    public static string Normalize(string? type)
    {
        var t = (type ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-');
        if (t.Length == 0) return Bug;
        if (t is "us" or "userstory" or "story") t = UserStory;
        if (!All.Contains(t))
            throw new DomainException($"Tipo de item inválido: '{type}' (use bug ou user-story).");
        return t;
    }
}

/// <summary>
/// 0085: tipos conhecidos de sub-tarefa (o catálogo com rótulo/ícone/parâmetros é configurável em
/// <c>WorkItemTaskKinds</c>; o domínio aceita qualquer slug para não travar um tipo novo do catálogo).
/// </summary>
public static class WorkTaskKind
{
    public const string Branch = "branch";
    public const string CherryPick = "cherry-pick";
    public const string Code = "code";
    public const string PullRequest = "pr";
    public const string DevOpsState = "devops-state";
    public const string SqlScript = "sql-script";
    public const string Validation = "validation";
    public const string Comment = "comment";
    public const string Manual = "manual";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>
        { Branch, CherryPick, Code, PullRequest, DevOpsState, SqlScript, Validation, Comment, Manual };

    /// <summary>Tipo da sub-tarefa que espelha uma etapa do plano (task/code/pr/ticket/question/validation).</summary>
    public static string FromStep(string stepKind, string executor) => stepKind switch
    {
        ExecutionStepKind.Code => Code,
        ExecutionStepKind.PullRequest => PullRequest,
        ExecutionStepKind.Validation => Validation,
        ExecutionStepKind.Ticket or ExecutionStepKind.Question => Manual,
        _ => executor == ExecutionExecutor.User ? Manual : Code,
    };
}

/// <summary>0085: tipos de registro do histórico do item.</summary>
public static class WorkItemLogKind
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Status = "status";
    public const string Task = "task";
    public const string Request = "request";
    public const string Cancel = "cancel";
    public const string Undo = "undo";
    public const string Info = "info";
}

/// <summary>0085: tipos de link do item.</summary>
public static class WorkItemLinkKind
{
    public const string PullRequest = "pr";
    public const string Branch = "branch";
    public const string Commit = "commit";
    public const string Document = "doc";
    public const string Attachment = "attachment";
    public const string Timeline = "timeline";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { PullRequest, Branch, Commit, Document, Attachment, Timeline };
}

/// <summary>0085: link do item (PR, branch, commit, documento, anexo, timeline) — JSON no próprio item.</summary>
public class WorkItemLink
{
    public string Kind { get; set; } = WorkItemLinkKind.Document;
    public string? Title { get; set; }
    public string Url { get; set; } = string.Empty;
}

/// <summary>0085: como desfazer a sub-tarefa concluída (ex.: <c>{action:"close-pr", params:{repository, number}}</c>).</summary>
public class WorkTaskUndo
{
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, string> Params { get; set; } = [];
}
