using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

public class ExecutionPlanSummaryResponse
{
    public Guid Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public string? StatusChangedBy { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public int StepsTotal { get; set; }
    public int StepsCompleted { get; set; }
}

public class ExecutionPlanResponse : ExecutionPlanSummaryResponse
{
    public string? Summary { get; set; }
    public List<ExecutionStepResponse> Steps { get; set; } = [];
    public List<ExecutionArtifactResponse> Artifacts { get; set; } = [];

    /// <summary>Id do último registro de andamento (cursor para buscar só os novos).</summary>
    public long LastLogId { get; set; }

    /// <summary>Hora do servidor na resposta — o front calcula "sem sinal há X min" sem depender do relógio local.</summary>
    public DateTimeOffset ServerTime { get; set; }
}

public class ExecutionStepResponse
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public string? StatusChangedBy { get; set; }
    public string? Activity { get; set; }
    public string? Checkpoint { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ExecutionLogResponse
{
    public long Id { get; set; }
    public string? StepKey { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ExecutionArtifactResponse
{
    public Guid Id { get; set; }
    public string? StepKey { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Resposta do heartbeat da skill: o que ela deve fazer agora.
/// <c>continue</c> = seguir; <c>wait</c> = plano pausado, esperar; <c>stop</c> = plano cancelado/concluído.
/// </summary>
public class ExecutionControlResponse
{
    public Guid PlanId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public string? StatusChangedBy { get; set; }
    public string Action { get; set; } = string.Empty;

    /// <summary>Etapas canceladas (pelo usuário ou pelo cancelamento do plano) — a skill pula.</summary>
    public List<string> CancelledSteps { get; set; } = [];
}

public static class ExecutionPlanMappings
{
    public static ExecutionPlanResponse ToResponse(this ExecutionPlan plan, IEnumerable<ExecutionArtifact> artifacts, long lastLogId, DateTimeOffset now)
    {
        var response = new ExecutionPlanResponse
        {
            Summary = plan.Summary,
            Steps = plan.Steps.OrderBy(s => s.Order).Select(s => s.ToResponse()).ToList(),
            Artifacts = artifacts.OrderBy(a => a.Kind).ThenBy(a => a.Name).Select(a => a.ToResponse()).ToList(),
            LastLogId = lastLogId,
            ServerTime = now
        };
        plan.FillSummary(response, plan.Steps.Count, plan.Steps.Count(s => s.Status == ExecutionStatus.Completed));
        return response;
    }

    public static T FillSummary<T>(this ExecutionPlan plan, T target, int stepsTotal, int stepsCompleted)
        where T : ExecutionPlanSummaryResponse
    {
        target.Id = plan.Id;
        target.CardNumber = plan.CardNumber;
        target.Kind = plan.Kind;
        target.Title = plan.Title;
        target.Status = plan.Status;
        target.StatusReason = plan.StatusReason;
        target.StatusChangedBy = plan.StatusChangedBy;
        target.CreatedBy = plan.CreatedBy;
        target.CreatedAt = plan.CreatedAt;
        target.UpdatedAt = plan.UpdatedAt;
        target.StartedAt = plan.StartedAt;
        target.FinishedAt = plan.FinishedAt;
        target.LastActivityAt = plan.LastActivityAt;
        target.StepsTotal = stepsTotal;
        target.StepsCompleted = stepsCompleted;
        return target;
    }

    public static ExecutionStepResponse ToResponse(this ExecutionStep step) => new()
    {
        Id = step.Id,
        Key = step.Key,
        Order = step.Order,
        Title = step.Title,
        Description = step.Description,
        Status = step.Status,
        StatusReason = step.StatusReason,
        StatusChangedBy = step.StatusChangedBy,
        Activity = step.Activity,
        Checkpoint = step.Checkpoint,
        StartedAt = step.StartedAt,
        FinishedAt = step.FinishedAt,
        UpdatedAt = step.UpdatedAt
    };

    public static ExecutionLogResponse ToResponse(this ExecutionLog log) => new()
    {
        Id = log.Id,
        StepKey = log.StepKey,
        Kind = log.Kind,
        Message = log.Message,
        CreatedAt = log.CreatedAt
    };

    public static ExecutionArtifactResponse ToResponse(this ExecutionArtifact artifact) => new()
    {
        Id = artifact.Id,
        StepKey = artifact.StepKey,
        Name = artifact.Name,
        Kind = artifact.Kind,
        ContentType = artifact.ContentType,
        Size = artifact.Size,
        Sha256 = artifact.Sha256,
        Description = artifact.Description,
        CreatedBy = artifact.CreatedBy,
        CreatedAt = artifact.CreatedAt,
        UpdatedAt = artifact.UpdatedAt
    };
}
