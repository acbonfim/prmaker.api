namespace solvace.prform.domain.Responses;

/// <summary>Estado da integração com o Teams para o usuário da requisição (GET /Teams/status).</summary>
public class TeamsStatusResponse
{
    /// <summary>O plugin "Teams Configurations" existe (não foi excluído).</summary>
    public bool Available { get; set; }

    /// <summary>O usuário salvou a URL do Workflow em "Minhas integrações".</summary>
    public bool Configured { get; set; }

    public int? PluginId { get; set; }
    public string GroupName { get; set; } = string.Empty;
}

public class TeamsApprovalResponse
{
    public bool Sent { get; set; }
    public string GroupName { get; set; } = string.Empty;
}
