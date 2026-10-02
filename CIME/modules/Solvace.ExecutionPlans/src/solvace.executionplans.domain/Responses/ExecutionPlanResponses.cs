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

    /// <summary>Sessão do Claude Code a retomar (0033); null = nenhuma registrada.</summary>
    public ExecutionSessionResponse? Executor { get; set; }
    /// <summary>Custo somado das sessões (0033).</summary>
    public ExecutionUsageResponse? Usage { get; set; }
    public DateTimeOffset? ResumeRequestedAt { get; set; }
    public string? ResumeRequestedBy { get; set; }
    public bool ResumePending { get; set; }

    /// <summary>Até qual comentário do card a skill já leu e quando (0037).</summary>
    public int? NotesReadNumber { get; set; }
    public DateTimeOffset? NotesReadAt { get; set; }

    /// <summary>Quantas coisas dependem do usuário agora (0037): perguntas abertas + etapas dele + etapas travadas esperando ele.</summary>
    public int UserPending { get; set; }
}

/// <summary>
/// Uma pendência do usuário (0037). <c>question</c> = pergunta aberta; <c>step</c> = etapa do usuário pronta ou em
/// andamento (concluir na tela); <c>unblock</c> = etapa travada esperando uma ação dele (o texto diz qual).
/// </summary>
public class ExecutionUserActionResponse
{
    public string Type { get; set; } = string.Empty;
    public string? StepKey { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Text { get; set; }
    public Guid? QuestionId { get; set; }
    public DateTimeOffset? Since { get; set; }
}

/// <summary>Plano ativo do usuário com pendência dele (0037) — lista de recentes, título da aba.</summary>
public class ExecutionUserPendingResponse
{
    public Guid PlanId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Phase { get; set; } = ExecutionPhase.Analysis;
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<ExecutionUserActionResponse> Actions { get; set; } = [];
}

public class ExecutionSessionResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string? Host { get; set; }
    public string? Cwd { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public class ExecutionUsageResponse
{
    public int Sessions { get; set; }
    public int Turns { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <summary>0041: chamadas pelo MCP e pelo script (somadas das sessões que informaram).</summary>
    public int McpCalls { get; set; }
    public int ScriptCalls { get; set; }
}

/// <summary>Consumo médio por plano, com MCP × sem MCP (0041).</summary>
public class ExecutionUsageReportResponse
{
    public int Days { get; set; }
    public bool AllUsers { get; set; }
    public List<ExecutionUsageReportRow> Rows { get; set; } = [];
}

public class ExecutionUsageReportRow
{
    /// <summary>mcp | script.</summary>
    public string Channel { get; set; } = string.Empty;
    /// <summary>analysis | correction | all.</summary>
    public string Phase { get; set; } = string.Empty;
    public int Plans { get; set; }
    public double AvgTurns { get; set; }
    /// <summary>Entrada + cache lido + cache escrito.</summary>
    public double AvgInputTokens { get; set; }
    public double AvgOutputTokens { get; set; }
    public double AvgMcpCalls { get; set; }
    public double AvgScriptCalls { get; set; }
}

/// <summary>Plano que o vigia local deve retomar (0033): pedido de "continuar" ou respostas chegadas pela tela.</summary>
public class ExecutionResumeCandidateResponse
{
    public Guid PlanId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Phase { get; set; } = ExecutionPhase.Analysis;
    public string Status { get; set; } = string.Empty;
    public ExecutionSessionResponse Session { get; set; } = new();
    /// <summary>resume-request | answers.</summary>
    public string Reason { get; set; } = string.Empty;
    public string? RequestedBy { get; set; }
    public DateTimeOffset Since { get; set; }
}

public class ExecutionPlanResponse : ExecutionPlanSummaryResponse
{
    public string? Summary { get; set; }
    public List<ExecutionStepResponse> Steps { get; set; } = [];
    public List<ExecutionArtifactResponse> Artifacts { get; set; } = [];
    public List<ExecutionQuestionResponse> Questions { get; set; } = [];
    public List<ExecutionLinkResponse> Links { get; set; } = [];

    /// <summary>O que depende do usuário agora, na ordem do plano (0037).</summary>
    public List<ExecutionUserActionResponse> UserActions { get; set; } = [];

    /// <summary>Comentários do card inteiro (análise e correção), com os anexos (0031).</summary>
    public List<ExecutionNoteResponse> Notes { get; set; } = [];

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
    /// <summary>Etapa "aguardando": user | answer | external (0037).</summary>
    public string? WaitingOn { get; set; }
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
    public Guid PlanId { get; set; }
    /// <summary>Número por card (<c>anexo #n</c>, 0031).</summary>
    public int Number { get; set; }
    /// <summary>Comentário a que pertence (0031); null = arquivo da skill.</summary>
    public Guid? NoteId { get; set; }
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

    /// <summary>Maior número de comentário do usuário no card (0031) — o vigia acorda quando muda.</summary>
    public int LastUserNoteNumber { get; set; }

    /// <summary>Última mudança (novo, editado, removido) em comentário do usuário no card (0031).</summary>
    public DateTimeOffset? UserNotesChangedAt { get; set; }

    /// <summary>Pendências do usuário (0037) — a skill diz no chat o que falta do lado dele.</summary>
    public int UserPending { get; set; }
    public List<ExecutionUserActionResponse> UserActions { get; set; } = [];
}

/// <summary>Comentário do plano (0031), com os anexos.</summary>
public class ExecutionNoteResponse
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    /// <summary>Fase do plano onde foi feito: analysis | correction.</summary>
    public string PlanPhase { get; set; } = ExecutionPhase.Analysis;
    public int Number { get; set; }
    public string? StepKey { get; set; }
    public string Text { get; set; } = string.Empty;
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public bool FromExecutor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<ExecutionArtifactResponse> Attachments { get; set; } = [];
}

public record ExecutionControlStep(string Key, string Status, string Executor, string? WaitingOn = null, string? Reason = null, string? ChangedBy = null);

public static class ExecutionPlanMappings
{
    public static ExecutionPlanResponse ToResponse(this ExecutionPlan plan, IEnumerable<ExecutionArtifact> artifacts, long lastLogId, DateTimeOffset now,
        IEnumerable<ExecutionQuestion>? questions = null, IEnumerable<ExecutionLink>? links = null, List<ExecutionNoteResponse>? notes = null)
    {
        var response = new ExecutionPlanResponse
        {
            Summary = plan.Summary,
            Steps = plan.Steps.OrderBy(s => s.Order).Select(s => s.ToResponse()).ToList(),
            Artifacts = artifacts.OrderBy(a => a.Kind).ThenBy(a => a.Name).Select(a => a.ToResponse()).ToList(),
            Questions = (questions ?? []).OrderBy(q => q.CreatedAt).ThenBy(q => q.Order).Select(q => q.ToResponse()).ToList(),
            Links = (links ?? []).OrderByDescending(l => l.CreatedAt).Select(l => l.ToResponse()).ToList(),
            UserActions = plan.UserActions(questions ?? []),
            LastLogId = lastLogId,
            ServerTime = now,
            Notes = notes ?? []
        };
        plan.FillSummary(response, plan.Steps.Count, plan.Steps.Count(s => s.Status == ExecutionStatus.Completed));
        response.UserPending = response.UserActions.Count;
        return response;
    }

    /// <summary>
    /// Pendências do usuário (0037): perguntas abertas primeiro (na ordem em que foram feitas), depois as etapas na
    /// ordem do plano. Etapa esperando só a resposta de uma pergunta não aparece duas vezes.
    /// </summary>
    public static List<ExecutionUserActionResponse> UserActions(this ExecutionPlan plan, IEnumerable<ExecutionQuestion> questions)
    {
        if (plan.IsFinished || plan.Status == ExecutionStatus.Failed)
            return [];
        var titles = plan.Steps.ToDictionary(s => s.Key, s => s.Title);
        var actions = questions
            .Where(q => q.Status == ExecutionQuestionStatus.Open)
            .OrderBy(q => q.CreatedAt).ThenBy(q => q.Order)
            .Select(q => new ExecutionUserActionResponse
            {
                Type = "question",
                StepKey = q.StepKey,
                Title = q.StepKey is not null && titles.TryGetValue(q.StepKey, out var t) ? t : "Pergunta",
                Text = q.Text,
                QuestionId = q.Id,
                Since = q.CreatedAt
            })
            .ToList();
        actions.AddRange(plan.StepsPendingForUser().Select(s => new ExecutionUserActionResponse
        {
            Type = s.Status == ExecutionStatus.Waiting ? "unblock" : "step",
            StepKey = s.Key,
            Title = s.Title,
            Text = s.Status == ExecutionStatus.Waiting ? s.StatusReason : null,
            Since = s.Status == ExecutionStatus.Pending ? null : s.UpdatedAt
        }));
        return actions;
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
        target.Executor = plan.CurrentSession?.ToResponse();
        target.Usage = plan.Sessions.Count == 0 ? null : new ExecutionUsageResponse
        {
            Sessions = plan.Sessions.Count,
            Turns = plan.Sessions.Sum(s => s.Turns),
            InputTokens = plan.Sessions.Sum(s => s.InputTokens),
            OutputTokens = plan.Sessions.Sum(s => s.OutputTokens),
            CacheReadTokens = plan.Sessions.Sum(s => s.CacheReadTokens),
            CacheWriteTokens = plan.Sessions.Sum(s => s.CacheWriteTokens),
            UpdatedAt = plan.Sessions.Max(s => s.UsageUpdatedAt),
            McpCalls = plan.Sessions.Sum(s => s.McpCalls ?? 0),
            ScriptCalls = plan.Sessions.Sum(s => s.ScriptCalls ?? 0)
        };
        target.ResumeRequestedAt = plan.ResumeRequestedAt;
        target.ResumeRequestedBy = plan.ResumeRequestedBy;
        target.ResumePending = plan.ResumePending;
        target.NotesReadNumber = plan.NotesReadNumber;
        target.NotesReadAt = plan.NotesReadAt;
        return target;
    }

    public static ExecutionSessionResponse ToResponse(this ExecutionSession session) => new()
    {
        SessionId = session.SessionId,
        Host = session.Host,
        Cwd = session.Cwd,
        StartedAt = session.StartedAt,
        LastSeenAt = session.LastSeenAt
    };

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
        WaitingOn = step.WaitingOn,
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
        PlanId = artifact.PlanId,
        Number = artifact.Number,
        NoteId = artifact.NoteId,
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

    public static ExecutionNoteResponse ToResponse(this ExecutionNote note, string planPhase, IEnumerable<ExecutionArtifact> attachments) => new()
    {
        Id = note.Id,
        PlanId = note.PlanId,
        PlanPhase = planPhase,
        Number = note.Number,
        StepKey = note.StepKey,
        Text = note.Text,
        AuthorUserId = note.AuthorUserId,
        AuthorName = note.AuthorName,
        FromExecutor = note.FromExecutor,
        CreatedAt = note.CreatedAt,
        UpdatedAt = note.UpdatedAt,
        Attachments = attachments.OrderBy(a => a.Number).Select(a => a.ToResponse()).ToList()
    };
}
