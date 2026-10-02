using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cime.ExecutionAgent;

/// <summary>Configuração local do executor (~/.prmake-agent/config.json, só o dono lê).</summary>
public sealed class AgentConfig
{
    public string ApiBase { get; set; } = "https://api.softhouse.app.br/api/v1";
    /// <summary>Credencial do executor (devolvida pelo register).</summary>
    public string? Token { get; set; }
    public Guid? WorkerId { get; set; }
    public string? Name { get; set; }
    /// <summary>Pasta onde o Claude roda (raiz dos repositórios).</summary>
    public string? Workspace { get; set; }
    public string? ClaudePath { get; set; }
    public int TimeoutMinutes { get; set; } = 120;
    public int PauseGraceMinutes { get; set; } = 10;
    public int WorktreeRetentionDays { get; set; } = 7;
    public bool AutoUpdate { get; set; } = true;
    public bool Notifications { get; set; } = true;
}

public sealed class RegisterRequest
{
    public string Host { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Os { get; set; }
    public string? AgentVersion { get; set; }
}

public sealed class RegistrationResponse
{
    public WorkerInfo Worker { get; set; } = new();
    public string Token { get; set; } = string.Empty;
}

public sealed class WorkerInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Online { get; set; }
    public int MaxConcurrency { get; set; }
    public string? AgentVersion { get; set; }
    public string? LatestAgentVersion { get; set; }
    public int Running { get; set; }
    public int DoctorProblems { get; set; }
}

public sealed class ReportRequest
{
    public string? AgentVersion { get; set; }
    public string? ClaudeVersion { get; set; }
    public string? SkillsVersion { get; set; }
    public string? Workspace { get; set; }
    public JsonElement? Capabilities { get; set; }
}

public sealed class WorkerState
{
    public string Status { get; set; } = "active";
    public int MaxConcurrency { get; set; } = 1;
    public string? LatestAgentVersion { get; set; }
    public bool DoctorRequested { get; set; }
    public List<Guid> ActiveRequestIds { get; set; } = [];
}

public sealed class DoctorCheck
{
    public string Name { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public string? Message { get; set; }
    public string? Severity { get; set; }
}

public sealed class DoctorRequest
{
    public List<DoctorCheck> Checks { get; set; } = [];
}

public sealed class RequestInfo
{
    public Guid Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? PlanId { get; set; }
    public string? SessionId { get; set; }
    public string? SessionHost { get; set; }
    public string? SessionCwd { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
}

public sealed class ClaimResponse
{
    public RequestInfo Request { get; set; } = new();
    public string ResumePrompt { get; set; } = string.Empty;
    public string FreshPrompt { get; set; } = string.Empty;
    public decimal? RemainingBudgetUsd { get; set; }
}

public sealed class StartRequest
{
    public int? Pid { get; set; }
    public string? SessionId { get; set; }
}

public sealed class HeartbeatRequest
{
    public int? Pid { get; set; }
    public string? StderrTail { get; set; }
}

public sealed class HeartbeatResponse
{
    public string Action { get; set; } = "continue";
    public string RequestStatus { get; set; } = string.Empty;
    public string? PlanStatus { get; set; }
    public string? Reason { get; set; }
}

public sealed class FinishRequest
{
    public string Outcome { get; set; } = "done";
    public int? ExitCode { get; set; }
    public string? Reason { get; set; }
    public string? Error { get; set; }
    public string? StderrTail { get; set; }
    public bool Retryable { get; set; } = true;
    /// <summary>total_cost_usd do Claude Code: ACUMULADO da sessão quando ela é retomada (o PRMake grava a diferença).</summary>
    public decimal? CostUsd { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public int? Turns { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    // 0044: partes da entrada desta execução, modelo e o consumo final da sessão (transcript) para o plano.
    public long? FreshInputTokens { get; set; }
    public long? CacheReadTokens { get; set; }
    public long? CacheWriteTokens { get; set; }
    public string? Model { get; set; }
    public string? SessionId { get; set; }
    public SessionUsage? SessionUsage { get; set; }
}

/// <summary>Consumo acumulado da sessão (mesmo formato do <c>PUT ExecutionPlan/{id}/usage</c> da skill).</summary>
public sealed class SessionUsage
{
    public string SessionId { get; set; } = string.Empty;
    public string? Host { get; set; }
    public int Turns { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public string? Model { get; set; }
    public int? McpCalls { get; set; }
    public int? ScriptCalls { get; set; }
    public int? KbCalls { get; set; }
    public int? SearchCalls { get; set; }
}

public sealed class PlanPending
{
    public string Status { get; set; } = string.Empty;
    public List<JsonElement> UserActions { get; set; } = [];
}

public sealed class AgentDescriptor
{
    public string? Version { get; set; }
    public List<string> Rids { get; set; } = [];
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AgentConfig))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(RegistrationResponse))]
[JsonSerializable(typeof(WorkerInfo))]
[JsonSerializable(typeof(ReportRequest))]
[JsonSerializable(typeof(WorkerState))]
[JsonSerializable(typeof(DoctorRequest))]
[JsonSerializable(typeof(ClaimResponse))]
[JsonSerializable(typeof(StartRequest))]
[JsonSerializable(typeof(HeartbeatRequest))]
[JsonSerializable(typeof(HeartbeatResponse))]
[JsonSerializable(typeof(FinishRequest))]
[JsonSerializable(typeof(PlanPending))]
[JsonSerializable(typeof(AgentDescriptor))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
public partial class AgentJson : JsonSerializerContext;

/// <summary>Config com indentação (arquivo editável à mão).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(AgentConfig))]
public partial class AgentConfigJson : JsonSerializerContext;
