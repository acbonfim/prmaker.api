using System.Text.Json;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

public class ExecutionRequestResponse
{
    public Guid Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? PlanId { get; set; }
    public string? SessionId { get; set; }
    public string? SessionHost { get; set; }
    public Guid OwnerUserId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public Guid? TargetWorkerId { get; set; }
    public Guid? WorkerId { get; set; }
    public string? WorkerName { get; set; }
    public string? Note { get; set; }
    public bool Force { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public DateTimeOffset? NotBefore { get; set; }
    public string? WaitReason { get; set; }
    /// <summary>Código do motivo de espera para a tela: no-worker | offline | paused | busy | budget | retry | gathering | null.</summary>
    public string? WaitCode { get; set; }
    public string? LastError { get; set; }
    public string? StderrTail { get; set; }
    public int? ExitCode { get; set; }
    public string? FinishedReason { get; set; }
    public string? FinishedBy { get; set; }
    /// <summary>Custo deste pedido (0044: já sem o acumulado das execuções anteriores da mesma sessão).</summary>
    public decimal? CostUsd { get; set; }
    /// <summary>Acumulado da sessão no fim do pedido (como o Claude Code informa).</summary>
    public decimal? SessionCostUsd { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long? FreshInputTokens { get; set; }
    public long? CacheReadTokens { get; set; }
    public long? CacheWriteTokens { get; set; }
    public string? Model { get; set; }
    public int? Turns { get; set; }
    /// <summary>0049: fase do card quando a máquina pegou o pedido (analysis | correction).</summary>
    public string? Phase { get; set; }
    /// <summary>0049: a correção começou numa sessão nova (não retomou a da análise).</summary>
    public bool NewSession { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
}

public class ExecutionWorkerResponse
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string? Os { get; set; }
    public string? AgentVersion { get; set; }
    public string? ClaudeVersion { get; set; }
    public string? SkillsVersion { get; set; }
    public string? Workspace { get; set; }
    public int MaxConcurrency { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Online { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public JsonElement? Capabilities { get; set; }
    public JsonElement? Doctor { get; set; }
    public DateTimeOffset? DoctorAt { get; set; }
    /// <summary>Diagnóstico pedido pela tela e ainda não recebido.</summary>
    public bool DoctorPending { get; set; }
    /// <summary>Limite de uso da conta do Claude atingido até este horário (null = livre).</summary>
    public DateTimeOffset? ThrottledUntil { get; set; }
    /// <summary>Checagens do doctor que falharam (resumo para a tela).</summary>
    public int DoctorProblems { get; set; }
    public int Running { get; set; }
    /// <summary>Versão do executor publicada pelo PRMake (para avisar "desatualizado").</summary>
    public string? LatestAgentVersion { get; set; }
}

/// <summary>Registro do executor: a credencial só aparece aqui (guarde-a).</summary>
public class ExecutionWorkerRegistrationResponse
{
    public ExecutionWorkerResponse Worker { get; set; } = new();
    public string Token { get; set; } = string.Empty;
}

/// <summary>Estado da fila no card: pedido ativo, últimos pedidos e as máquinas de quem está vendo.</summary>
public class ExecutionCardQueueResponse
{
    public ExecutionRequestResponse? Active { get; set; }
    public List<ExecutionRequestResponse> Recent { get; set; } = [];
    public List<ExecutionWorkerResponse> MyWorkers { get; set; } = [];
    /// <summary>Máquinas do dono do pedido ativo (quando não é quem está vendo).</summary>
    public List<ExecutionWorkerResponse> OwnerWorkers { get; set; } = [];
}

/// <summary>O que o executor recebe ao pegar um pedido.</summary>
public class ExecutionClaimResponse
{
    public ExecutionRequestResponse Request { get; set; } = new();
    /// <summary>Prompt para continuar a sessão (<c>claude --resume</c>).</summary>
    public string ResumePrompt { get; set; } = string.Empty;
    /// <summary>Prompt de uma sessão nova (sem a sessão nesta máquina ou pedido de análise).</summary>
    public string FreshPrompt { get; set; } = string.Empty;
    /// <summary>Saldo do orçamento do dia (para <c>--max-budget-usd</c>); null = sem limite.</summary>
    public decimal? RemainingBudgetUsd { get; set; }
    /// <summary>
    /// 0047: fase do card nesta execução (analysis | correction) e o modelo que o executor passa ao Claude Code
    /// (<c>--model</c>; ex.: <c>opus</c> na análise, <c>sonnet</c> na correção). Modelo null = o padrão da máquina.
    /// </summary>
    public string Phase { get; set; } = ExecutionPhase.Analysis;
    public string? Model { get; set; }
}

public class ExecutionHeartbeatResponse
{
    /// <summary>continue | cancel.</summary>
    public string Action { get; set; } = "continue";
    public string RequestStatus { get; set; } = string.Empty;
    public string? PlanStatus { get; set; }
    public string? Reason { get; set; }
}

public class ExecutionWorkerStateResponse
{
    public string Status { get; set; } = string.Empty;
    public int MaxConcurrency { get; set; }
    public string? LatestAgentVersion { get; set; }
    /// <summary>A tela pediu "rodar diagnóstico agora".</summary>
    public bool DoctorRequested { get; set; }
    /// <summary>Pedidos que o PRMake ainda considera deste executor (o executor reconcilia com os processos dele).</summary>
    public List<Guid> ActiveRequestIds { get; set; } = [];
}

public class ExecutionUserSettingsResponse
{
    public decimal? DailyBudgetUsd { get; set; }
    public decimal SpentTodayUsd { get; set; }
    public bool AutoAnalyzeEnabled { get; set; }
    public List<string> AutoWorkItemTypes { get; set; } = [];
    public List<string> AutoStates { get; set; } = [];
    public List<string> AutoAreaPaths { get; set; } = [];
    public string? AutoAssignedTo { get; set; }
    public int AutoMaxPerDay { get; set; }
    public DateTimeOffset? AutoLastCheckAt { get; set; }
    public string? AutoLastError { get; set; }
}

public static class ExecutionQueueResponseExtensions
{
    public static ExecutionRequestResponse ToResponse(this ExecutionRequest r) => new()
    {
        Id = r.Id,
        CardNumber = r.CardNumber,
        Kind = r.Kind,
        Source = r.Source,
        Status = r.Status,
        PlanId = r.PlanId,
        SessionId = r.SessionId,
        SessionHost = r.SessionHost,
        OwnerUserId = r.OwnerUserId,
        OwnerName = r.OwnerName,
        RequestedBy = r.RequestedBy,
        TargetWorkerId = r.TargetWorkerId,
        WorkerId = r.WorkerId,
        WorkerName = r.WorkerName,
        Note = r.Note,
        Force = r.Force,
        Attempts = r.Attempts,
        MaxAttempts = r.MaxAttempts,
        NotBefore = r.NotBefore,
        WaitReason = r.WaitReason,
        LastError = r.LastError,
        StderrTail = r.StderrTail,
        ExitCode = r.ExitCode,
        FinishedReason = r.FinishedReason,
        FinishedBy = r.FinishedBy,
        CostUsd = r.CostUsd,
        SessionCostUsd = r.SessionCostUsd,
        InputTokens = r.InputTokens,
        OutputTokens = r.OutputTokens,
        FreshInputTokens = r.FreshInputTokens,
        CacheReadTokens = r.CacheReadTokens,
        CacheWriteTokens = r.CacheWriteTokens,
        Model = r.Model,
        Turns = r.Turns,
        Phase = r.Phase,
        NewSession = r.NewSession,
        CreatedAt = r.CreatedAt,
        ClaimedAt = r.ClaimedAt,
        StartedAt = r.StartedAt,
        FinishedAt = r.FinishedAt,
        LastHeartbeatAt = r.LastHeartbeatAt
    };

    public static ExecutionWorkerResponse ToResponse(this ExecutionWorker w, DateTimeOffset now, int running = 0, string? latestAgentVersion = null)
    {
        var doctor = Parse(w.Doctor);
        return new ExecutionWorkerResponse
        {
            Id = w.Id,
            OwnerUserId = w.OwnerUserId,
            OwnerName = w.OwnerName,
            Name = w.Name,
            Host = w.Host,
            Os = w.Os,
            AgentVersion = w.AgentVersion,
            ClaudeVersion = w.ClaudeVersion,
            SkillsVersion = w.SkillsVersion,
            Workspace = w.Workspace,
            MaxConcurrency = w.MaxConcurrency,
            Status = w.Status,
            Online = w.IsOnline(now),
            LastSeenAt = w.LastSeenAt,
            CreatedAt = w.CreatedAt,
            Capabilities = Parse(w.Capabilities),
            Doctor = doctor,
            DoctorAt = w.DoctorAt,
            DoctorPending = w.DoctorPending,
            ThrottledUntil = w.IsThrottled(now) ? w.ThrottledUntil : null,
            DoctorProblems = doctor is { ValueKind: JsonValueKind.Array } arr
                ? arr.EnumerateArray().Count(c => c.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                : 0,
            Running = running,
            LatestAgentVersion = latestAgentVersion
        };
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
