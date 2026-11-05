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
    public DevOpsDevTestInQaConfig DevTestInQa { get; set; } = new();
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

/// <summary>Dev validando em QA: estado e coluna do board (ex.: "In Development (doing)" · "Dev Test in QA").</summary>
public class DevOpsDevTestInQaConfig
{
    public string? State { get; set; }
    public string? Column { get; set; }
}

public class DevOpsInitialEstimateConfig
{
    /// <summary>O usuário salvou os três valores (numéricos) em "Minhas integrações".</summary>
    public bool Configured { get; set; }
    public decimal? OriginalEstimate { get; set; }
    public decimal? RemainingWork { get; set; }
    public decimal? CompletedWork { get; set; }
}

/// <summary>Opção de classificação do card (feature 0028).</summary>
public class DevOpsClassificationPreset
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ResolutionType { get; set; } = string.Empty;
    public string GeneralClassification { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;

    /// <summary>Padrão de tratamento da skill analisar-bug (A–H) a que a opção costuma corresponder.</summary>
    public string? Pattern { get; set; }
}

/// <summary>Resultado de uma ação no card.</summary>
/// <summary>0078: horas do card no DevOps.</summary>
public class DevOpsEffortResponse
{
    public decimal? OriginalEstimate { get; set; }
    public decimal? CompletedWork { get; set; }
    public decimal? RemainingWork { get; set; }
}

public class DevOpsActionResponse
{
    public long Rev { get; set; }

    /// <summary>Texto curto do que foi feito (snackbar e Timeline).</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>0085 r3: estado de um tipo de work item no DevOps (nome, categoria e cor do processo).</summary>
public class WorkItemTypeState
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Categoria do processo: Proposed, InProgress, Resolved, Completed, Removed (pode faltar).</summary>
    public string? Category { get; set; }
    public string? Color { get; set; }
}

/// <summary>0085 r3: <c>GET Azure/card/{id}/states</c> — estados válidos do tipo do card, o atual e as sugestões.</summary>
public class CardStatesResponse
{
    /// <summary>Tipo do card no DevOps (System.WorkItemType).</summary>
    public string Type { get; set; } = string.Empty;
    public string? Current { get; set; }
    public List<CardStateOption> States { get; set; } = [];
}

public class CardStateOption
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }

    /// <summary>Próximo estado usual a partir do atual (ações configuradas ou a categoria seguinte).</summary>
    public bool Suggested { get; set; }

    /// <summary>Ação do menu "Ações DevOps" que leva a este estado (dev-test-in-qa, ready-for-qa, test-in-production).</summary>
    public string? Action { get; set; }

    public bool Current { get; set; }
}
