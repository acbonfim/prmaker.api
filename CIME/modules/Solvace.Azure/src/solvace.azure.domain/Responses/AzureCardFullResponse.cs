using System.Text.Json;

namespace solvace.azure.domain.Models;

/// <summary>
/// Resposta consolidada com todas as informações possíveis de um card do Azure DevOps:
/// todos os campos (incluindo custom fields), histórico de alterações, comentários e
/// um resumo de alertas de preenchimento.
/// </summary>
public class AzureCardFullResponse
{
    public long Id { get; set; }
    public long Rev { get; set; }
    public string? Url { get; set; }

    /// <summary>Todos os campos do work item, com a chave sendo o reference name do DevOps.</summary>
    public Dictionary<string, JsonElement> Fields { get; set; } = new();

    public List<AzureCardComment> Comments { get; set; } = new();
    public List<AzureCardHistory> History { get; set; } = new();

    public AzureCardAlerts Alerts { get; set; } = new();

    public string? Error { get; set; }
}

public class AzureCardComment
{
    public long Id { get; set; }
    public string? Text { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public DateTimeOffset? ModifiedDate { get; set; }
}

public class AzureCardHistory
{
    public int Rev { get; set; }
    public string? ChangedByName { get; set; }
    public DateTimeOffset? ChangedDate { get; set; }
    public List<AzureCardFieldChange> Changes { get; set; } = new();
}

public class AzureCardFieldChange
{
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

/// <summary>Sinalizadores de preenchimento do card usados na barra de alertas.</summary>
public class AzureCardAlerts
{
    public bool MissingRootCause { get; set; }
    public bool MissingResolutionType { get; set; }
    public bool MissingGeneralClassification { get; set; }
    public bool MissingClassification { get; set; }
    public bool RemainingNotZero { get; set; }
}
