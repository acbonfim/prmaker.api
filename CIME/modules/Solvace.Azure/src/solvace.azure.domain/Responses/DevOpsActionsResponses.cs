namespace solvace.azure.domain.Models;

/// <summary>Valores efetivos das ações de DevOps do usuário (menu "Ações DevOps", feature 0011).</summary>
public class DevOpsActionsConfigResponse
{
    /// <summary>O plugin "AI Configurations" existe e foi lido.</summary>
    public bool Available { get; set; }

    public DevOpsBugActionsConfig Bug { get; set; } = new();
}

public class DevOpsBugActionsConfig
{
    /// <summary>Prompt do resumo não técnico (vazio = opção indisponível).</summary>
    public string? SummaryPrompt { get; set; }

    public DevOpsTestInProductionConfig TestInProduction { get; set; } = new();
    public DevOpsReadyForQaConfig ReadyForQa { get; set; } = new();
    public DevOpsInitialEstimateConfig InitialEstimate { get; set; } = new();
}

public class DevOpsTestInProductionConfig
{
    /// <summary>Área em que o card precisa estar (vazio = sem restrição).</summary>
    public string? RequiredArea { get; set; }
    public string? Area { get; set; }
    public string? State { get; set; }
    public string? Comment { get; set; }
}

public class DevOpsReadyForQaConfig
{
    public string? State { get; set; }
}

public class DevOpsInitialEstimateConfig
{
    /// <summary>O usuário salvou os três valores (numéricos) em "Minhas integrações".</summary>
    public bool Configured { get; set; }
    public decimal? OriginalEstimate { get; set; }
    public decimal? RemainingWork { get; set; }
    public decimal? CompletedWork { get; set; }
}

/// <summary>Resultado de uma ação no card.</summary>
public class DevOpsActionResponse
{
    public long Rev { get; set; }

    /// <summary>Texto curto do que foi feito (snackbar e Timeline).</summary>
    public string Message { get; set; } = string.Empty;
}
