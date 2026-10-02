namespace solvace.executionplans.domain.Requests;

/// <summary>"Analisar com Claude" / "Continuar com Claude" (0039).</summary>
public class CreateExecutionRequestRequest
{
    public string? CardNumber { get; set; }
    /// <summary>analyze | resume; vazio = resume se o card tem plano aberto, senão analyze.</summary>
    public string? Kind { get; set; }
    /// <summary>Máquina escolhida; vazio = qualquer máquina do dono.</summary>
    public Guid? TargetWorkerId { get; set; }
    /// <summary>Observação de quem pediu (entra no prompt da análise).</summary>
    public string? Note { get; set; }
    /// <summary>Rodar mesmo com o orçamento do dia estourado.</summary>
    public bool Force { get; set; }
}

public class CancelExecutionRequestRequest
{
    public string? Reason { get; set; }
}

/// <summary>O executor se registra com a api-key do usuário e recebe a credencial dele.</summary>
public class RegisterExecutionWorkerRequest
{
    public string? Host { get; set; }
    public string? Name { get; set; }
    public string? Os { get; set; }
    public string? AgentVersion { get; set; }
}

/// <summary>Sinal de vida do executor (sem pedido), com o que ele sabe sobre si.</summary>
public class ExecutionWorkerReportRequest
{
    public string? AgentVersion { get; set; }
    public string? ClaudeVersion { get; set; }
    public string? SkillsVersion { get; set; }
    public string? Workspace { get; set; }
    /// <summary>JSON livre: repositórios mapeados, aliases de banco alcançáveis...</summary>
    public System.Text.Json.JsonElement? Capabilities { get; set; }
}

public class ExecutionWorkerDoctorRequest
{
    public List<ExecutionDoctorCheck> Checks { get; set; } = [];
}

public class ExecutionDoctorCheck
{
    public string Name { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public string? Message { get; set; }
    /// <summary>error (bloqueia análises) | warning.</summary>
    public string? Severity { get; set; }
}

public class ConfigureExecutionWorkerRequest
{
    public string? Name { get; set; }
    public int? MaxConcurrency { get; set; }
}

public class StartExecutionRequestRequest
{
    public int? Pid { get; set; }
    public string? SessionId { get; set; }
}

public class ExecutionRequestHeartbeatRequest
{
    public int? Pid { get; set; }
    public string? StderrTail { get; set; }
}

public class FinishExecutionRequestRequest
{
    /// <summary>done | failed.</summary>
    public string? Outcome { get; set; }
    public int? ExitCode { get; set; }
    public string? Reason { get; set; }
    public string? Error { get; set; }
    public string? StderrTail { get; set; }
    /// <summary>Falha passageira (rede, processo morto): volta para a fila com espera.</summary>
    public bool Retryable { get; set; } = true;
    /// <summary>total_cost_usd do Claude Code — ACUMULADO da sessão quando ela é retomada (o PRMake grava a diferença).</summary>
    public decimal? CostUsd { get; set; }
    /// <summary>Entrada total (nova + cache lido + cache escrito) desta execução.</summary>
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public int? Turns { get; set; }
    /// <summary>0044 (executor 1.0.3+): as partes da entrada desta execução e o modelo.</summary>
    public long? FreshInputTokens { get; set; }
    public long? CacheReadTokens { get; set; }
    public long? CacheWriteTokens { get; set; }
    public string? Model { get; set; }
    /// <summary>0044: sessão e consumo final dela lido do transcript (o mesmo cálculo da skill) — vai para o plano.</summary>
    public string? SessionId { get; set; }
    public RecordExecutionUsageRequest? SessionUsage { get; set; }
    /// <summary>0041: limite de uso da conta do Claude — o pedido espera até aqui sem gastar tentativa.</summary>
    public DateTimeOffset? RetryAt { get; set; }
}

public class UpdateExecutionUserSettingsRequest
{
    public decimal? DailyBudgetUsd { get; set; }
    public bool AutoAnalyzeEnabled { get; set; }
    public List<string>? AutoWorkItemTypes { get; set; }
    public List<string>? AutoStates { get; set; }
    public List<string>? AutoAreaPaths { get; set; }
    public string? AutoAssignedTo { get; set; }
    public int? AutoMaxPerDay { get; set; }
}
