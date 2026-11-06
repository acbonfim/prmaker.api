using System.Text.Json;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>0085: item de trabalho como a tela/MCP mostram (JSON camelCase).</summary>
public class WorkItemResponse
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string Type { get; set; } = WorkItemType.Task;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = WorkItemStatus.Pending;
    public string Origin { get; set; } = WorkItemOrigin.User;
    public string? TemplateKey { get; set; }
    public Guid? PlanId { get; set; }
    public string? PlanPhase { get; set; }
    public Guid? CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CancelReason { get; set; }
    /// <summary>Pode editar: item pendente com todas as sub-tarefas pendentes.</summary>
    public bool Editable { get; set; }
    public WorkItemProgress Progress { get; set; } = new();
    public List<WorkItemLink> Links { get; set; } = [];
    /// <summary>Pedido de execução ativo do item (fila).</summary>
    public Guid? RequestId { get; set; }
    /// <summary>O que o Claude está fazendo agora no pedido do item.</summary>
    public string? Activity { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public List<WorkTaskResponse> Tasks { get; set; } = [];
    /// <summary>Histórico (só no GET por número).</summary>
    public List<WorkItemLogResponse>? Logs { get; set; }
    /// <summary>0091: o que o item e as sub-tarefas geraram (só no GET por número).</summary>
    public List<WorkDeliverableResponse>? Deliverables { get; set; }

    public static WorkItemResponse From(WorkItem item, string? planPhase = null, string? activity = null) => new()
    {
        Id = item.Id,
        Number = item.Number,
        CardNumber = item.CardNumber,
        Type = item.Type,
        Title = item.Title,
        Description = item.Description,
        Status = item.Status,
        Origin = item.Origin,
        TemplateKey = item.TemplateKey,
        PlanId = item.PlanId,
        PlanPhase = planPhase,
        CreatedBy = item.CreatedBy,
        CreatedByName = item.CreatedByName,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
        StartedAt = item.StartedAt,
        CompletedAt = item.CompletedAt,
        CancelReason = item.CancelReason,
        Editable = item.Editable,
        Progress = new WorkItemProgress
        {
            Done = item.Tasks.Count(t => WorkTaskStatus.IsDone(t.Status)),
            Total = item.Tasks.Count(t => t.Status is not (WorkTaskStatus.Cancelled or WorkTaskStatus.Reverted)),
        },
        Links = item.Links.ToList(),
        RequestId = item.ActiveRequestId,
        Activity = activity,
        Fingerprint = item.Fingerprint,
        Tasks = item.Tasks.OrderBy(t => t.Order).Select(WorkTaskResponse.From).ToList(),
    };
}

public class WorkItemProgress
{
    public int Done { get; set; }
    public int Total { get; set; }
}

/// <summary>0085: sub-tarefa do item.</summary>
public class WorkTaskResponse
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Executor { get; set; } = ExecutionExecutor.Claude;
    public Dictionary<string, string> Params { get; set; } = [];
    public string Status { get; set; } = WorkTaskStatus.Pending;
    public string? StepKey { get; set; }
    public string? Result { get; set; }
    /// <summary>Saídas (objeto JSON livre; <c>{}</c> quando não há).</summary>
    public JsonElement Output { get; set; }
    public WorkTaskUndo? Undo { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public static WorkTaskResponse From(WorkItemTask task) => new()
    {
        Id = task.Id,
        Number = task.Number,
        Order = task.Order,
        Title = task.Title,
        Description = task.Description,
        Kind = task.Kind,
        Executor = task.Executor,
        Params = task.Params,
        Status = task.Status,
        StepKey = task.StepKey,
        Result = task.Result,
        Output = task.OutputElement,
        Undo = task.Undo,
        StartedAt = task.StartedAt,
        CompletedAt = task.CompletedAt,
    };
}

public class WorkItemLogResponse
{
    public long Id { get; set; }
    public long? TaskNumber { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    public static WorkItemLogResponse From(WorkItemLog log) => new()
    {
        Id = log.Id,
        TaskNumber = log.TaskNumber,
        At = log.At,
        Actor = log.Actor,
        Kind = log.Kind,
        Text = log.Text,
    };
}

/// <summary>0085: <c>POST WorkItems/{number}/execute</c>.</summary>
public class WorkItemExecuteResponse
{
    public Guid? RequestId { get; set; }
    public string Status { get; set; } = string.Empty;
    public WorkItemResponse? Item { get; set; }
}
