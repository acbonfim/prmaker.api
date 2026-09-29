namespace solvace.executionplans.domain.Requests;

/// <summary>Definição de uma etapa enviada pela skill (upsert pela <see cref="Key"/>).</summary>
public class ExecutionStepDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateExecutionPlanRequest
{
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Quem gerou o plano (ex.: "analisar-bug").</summary>
    public string Kind { get; set; } = "analisar-bug";

    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public List<ExecutionStepDefinition> Steps { get; set; } = [];
}

public class UpsertExecutionStepsRequest
{
    public List<ExecutionStepDefinition> Steps { get; set; } = [];
}

/// <summary>Atualização parcial de uma etapa: só os campos informados mudam.</summary>
public class UpdateExecutionStepRequest
{
    public string? Status { get; set; }
    public string? Reason { get; set; }
    public string? Activity { get; set; }
    public string? Checkpoint { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
}

public class CancelExecutionStepRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class ExecutionLogItem
{
    /// <summary>Id gerado por quem envia: reenviar o mesmo pedaço (fila local da skill) não duplica.</summary>
    public string? ClientId { get; set; }
    public string? StepKey { get; set; }
    public string? Kind { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AppendExecutionLogsRequest
{
    public List<ExecutionLogItem> Logs { get; set; } = [];
}

public class ChangeExecutionPlanStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }

    /// <summary>Resumo final (opcional; a skill manda ao concluir).</summary>
    public string? Summary { get; set; }
}
