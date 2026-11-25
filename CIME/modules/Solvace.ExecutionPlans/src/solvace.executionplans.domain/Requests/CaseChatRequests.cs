using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Requests;

/// <summary>0080: mensagem de uma pessoa (o resto vem como multipart: arquivos e áudio).</summary>
public class PostCaseMessageRequest
{
    public string? Text { get; set; }
    /// <summary>Mensagem de voz: a transcrição revisada (o áudio vai nos arquivos).</summary>
    public string? Transcript { get; set; }
    public bool Voice { get; set; }
    public bool PeopleOnly { get; set; }
    public bool Deep { get; set; }
    public int? ReplyTo { get; set; }
}

public class EditCaseMessageRequest
{
    public string? Text { get; set; }
}

/// <summary>Resposta do especialista (MCP/REST com o token do caso).</summary>
public class CaseReplyRequest
{
    public string? Text { get; set; }
    public List<string>? Steps { get; set; }
    public int? ReplyTo { get; set; }
    /// <summary>Arquivos que ele subiu antes (números) e entram nesta mensagem.</summary>
    public List<int>? Files { get; set; }
}

public class CasePointRequest
{
    public string? Text { get; set; }
    public string? Status { get; set; }
    public int? MessageNumber { get; set; }
}

public class CaseProposalRequest
{
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Phase { get; set; }
    public List<CaseProposalChange>? Changes { get; set; }
    /// <summary>Texto da mensagem que acompanha o cartão ("Chegamos a uma conclusão...").</summary>
    public string? Message { get; set; }
}

public class CaseProposalDecisionRequest
{
    public bool Accept { get; set; }
    /// <summary>Aceitar e já executar a nova versão.</summary>
    public bool Execute { get; set; }
    public string? Reason { get; set; }
}

public class CaseActionRequest
{
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public string? Message { get; set; }
}

public class CaseActionDecisionRequest
{
    public bool Confirm { get; set; }
}

public class CaseActionFinishRequest
{
    public bool Ok { get; set; } = true;
    public string? Result { get; set; }
}
