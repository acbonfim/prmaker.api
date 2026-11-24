namespace solvace.executionplans.domain.Requests;

/// <summary>0099: corpo do <c>POST Epics</c> / <c>PUT Epics/{number}</c> (campos null limpam no PUT, título obrigatório).</summary>
public class SaveEpicRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// <summary>Cor hexa (<c>#7a5cff</c>); vazio = a padrão.</summary>
    public string? Color { get; set; }
    public List<string>? Tags { get; set; }
    public string? DevOpsTeam { get; set; }
    public string? DevOpsBoard { get; set; }
    /// <summary>Card do DevOps que representa o épico (Epic/Feature), opcional.</summary>
    public string? DevOpsCardId { get; set; }
}

/// <summary>0099: <c>POST Epics/{number}/status</c>.</summary>
public class EpicStatusRequest
{
    /// <summary>active | paused | done | archived.</summary>
    public string? Status { get; set; }
}

/// <summary>
/// 0099: <c>POST Epics/{number}/cards</c> — põe um card do DevOps no épico. <c>role</c> padrão <c>linked</c>; quem cria o
/// card pela Mesa (DevOps, B2) manda <c>created</c> com a foto (título/tipo).
/// </summary>
public class AddEpicCardRequest
{
    public string? Card { get; set; }
    /// <summary>created | linked | mentioned (padrão linked).</summary>
    public string? Role { get; set; }
    public string? Title { get; set; }
    public string? WorkItemType { get; set; }
}
