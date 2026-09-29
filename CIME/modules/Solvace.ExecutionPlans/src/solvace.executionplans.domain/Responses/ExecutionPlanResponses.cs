using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;

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

    /// <summary>analysis | correction (0024).</summary>
    public string Phase { get; set; } = ExecutionPhase.Analysis;
    public Guid? ParentPlanId { get; set; }
}

public class ExecutionPlanResponse : ExecutionPlanSummaryResponse
{
    public string? Summary { get; set; }
    public List<ExecutionStepResponse> Steps { get; set; } = [];
    public List<ExecutionArtifactResponse> Artifacts { get; set; } = [];
    public List<ExecutionQuestionResponse> Questions { get; set; } = [];
    public List<ExecutionLinkResponse> Links { get; set; } = [];

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
    public string Executor { get; set; } = ExecutionExecutor.Claude;
    public string Kind { get; set; } = ExecutionStepKind.Task;
    public string? Repository { get; set; }
    public List<string> DependsOn { get; set; } = [];
}

public class ExecutionQuestionResponse
{
    public Guid Id { get; set; }
    public string? StepKey { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<ExecutionQuestionOption> Options { get; set; } = [];
    public bool AllowFreeText { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public string? AnsweredBy { get; set; }
    public string? AnsweredVia { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ExecutionLinkResponse
{
    public Guid Id { get; set; }
    public string StepKey { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Status { get; set; }
    public bool BlocksStep { get; set; }
    public int? PullRequestNumber { get; set; }
    public string? Repository { get; set; }
    public string? TargetBranch { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? StatusChangedBy { get; set; }
    public DateTimeOffset? StatusChangedAt { get; set; }
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

    /// <summary>Pendentes com as dependências terminadas — a skill escolhe a próxima daqui (0024).</summary>
    public List<string> ReadySteps { get; set; } = [];

    /// <summary>Aguardando algo externo (resposta, chamado, merge) — a skill não mexe nelas (0024).</summary>
    public List<string> WaitingSteps { get; set; } = [];

    /// <summary>Todas as etapas, compacto (key, status, executor) — a skill vê o que o usuário concluiu na tela (0024).</summary>
    public List<ExecutionControlStep> Steps { get; set; } = [];

    /// <summary>Perguntas ainda sem resposta (0024).</summary>
    public int OpenQuestions { get; set; }
}

public record ExecutionControlStep(string Key, string Status, string Executor);

public static class ExecutionPlanMappings
{
    public static ExecutionPlanResponse ToResponse(this ExecutionPlan plan, IEnumerable<ExecutionArtifact> artifacts, long lastLogId, DateTimeOffset now,
        IEnumerable<ExecutionQuestion>? questions = null, IEnumerable<ExecutionLink>? links = null)
    {
        var response = new ExecutionPlanResponse
        {
            Summary = plan.Summary,
            Steps = plan.Steps.OrderBy(s => s.Order).Select(s => s.ToResponse()).ToList(),
            Artifacts = artifacts.OrderBy(a => a.Kind).ThenBy(a => a.Name).Select(a => a.ToResponse()).ToList(),
            Questions = (questions ?? []).OrderBy(q => q.CreatedAt).ThenBy(q => q.Order).Select(q => q.ToResponse()).ToList(),
            Links = (links ?? []).OrderByDescending(l => l.CreatedAt).Select(l => l.ToResponse()).ToList(),
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
        target.Phase = plan.Phase;
        target.ParentPlanId = plan.ParentPlanId;
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
        UpdatedAt = step.UpdatedAt,
        Executor = step.Executor,
        Kind = step.Kind,
        Repository = step.Repository,
        DependsOn = step.DependsOn.ToList()
    };

    public static ExecutionQuestionResponse ToResponse(this ExecutionQuestion q) => new()
    {
        Id = q.Id,
        StepKey = q.StepKey,
        Order = q.Order,
        Text = q.Text,
        Options = q.Options.ToList(),
        AllowFreeText = q.AllowFreeText,
        Status = q.Status,
        Answer = q.Answer,
        AnsweredBy = q.AnsweredBy,
        AnsweredVia = q.AnsweredVia,
        AnsweredAt = q.AnsweredAt,
        CreatedBy = q.CreatedBy,
        CreatedAt = q.CreatedAt
    };

    public static ExecutionLinkResponse ToResponse(this ExecutionLink l) => new()
    {
        Id = l.Id,
        StepKey = l.StepKey,
        Kind = l.Kind,
        Url = l.Url,
        Title = l.Title,
        Status = l.Status,
        BlocksStep = l.BlocksStep,
        PullRequestNumber = l.PullRequestNumber,
        Repository = l.Repository,
        TargetBranch = l.TargetBranch,
        CreatedBy = l.CreatedBy,
        CreatedAt = l.CreatedAt,
        StatusChangedBy = l.StatusChangedBy,
        StatusChangedAt = l.StatusChangedAt
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
