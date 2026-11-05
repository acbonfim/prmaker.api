using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>0080: a conversa do caso como a tela mostra.</summary>
public class CaseChatResponse
{
    public string CardNumber { get; set; } = string.Empty;
    public List<CaseMessageResponse> Messages { get; set; } = [];
    /// <summary>Há mensagens mais antigas que as devolvidas.</summary>
    public bool HasMore { get; set; }
    public List<CasePointResponse> Points { get; set; } = [];
    public List<CaseProposalResponse> Proposals { get; set; } = [];
    public List<CaseActionResponse> Actions { get; set; } = [];
    public List<CaseFileResponse> Files { get; set; } = [];
    public List<CaseParticipantResponse> Participants { get; set; } = [];
    /// <summary>Comentários antigos dos planos (histórico, só leitura).</summary>
    public List<CasePlanNoteResponse> PlanNotes { get; set; } = [];
    /// <summary>O que o especialista sabe do caso (chips).</summary>
    public List<string> Knows { get; set; } = [];
    /// <summary>Pedido do especialista (o ativo, senão o último).</summary>
    public ExecutionRequestResponse? Run { get; set; }
    public string? SessionHost { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    /// <summary>Quem está vendo pode aceitar propostas (dono do plano, admin, gestor).</summary>
    public bool CanDecide { get; set; }
    /// <summary>O card tem plano (proposta aceita e "Levar para o plano" precisam dele).</summary>
    public bool HasPlan { get; set; }
    public DateTimeOffset ServerTime { get; set; }
}

public class CaseMessageResponse
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string AuthorKind { get; set; } = CaseAuthorKind.Person;
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Kind { get; set; } = CaseMessageKind.Text;
    public string Text { get; set; } = string.Empty;
    public string? Transcript { get; set; }
    public bool PeopleOnly { get; set; }
    public bool Deep { get; set; }
    public List<string> Mentions { get; set; } = [];
    public List<string> Steps { get; set; } = [];
    public int? ReplyToNumber { get; set; }
    public Guid? ProposalId { get; set; }
    public Guid? ActionId { get; set; }
    public string? Model { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public int? DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public List<CaseFileResponse> Files { get; set; } = [];
}

public class CaseFileResponse
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public Guid? MessageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "file";
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public bool FromSpecialist { get; set; }
    /// <summary>0085 r5 (1.7): áudio da mensagem de voz — só toca na mensagem (não vem em <c>files</c> da conversa).</summary>
    public bool Hidden { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CasePointResponse
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Status { get; set; } = CasePointStatus.Agreed;
    public int? MessageNumber { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public bool FromSpecialist { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class CaseProposalResponse
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string Phase { get; set; } = ExecutionPhase.Correction;
    public List<CaseProposalChange> Changes { get; set; } = [];
    public string Status { get; set; } = CaseProposalStatus.Pending;
    public bool Execute { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? RejectReason { get; set; }
    public int? PlanNoteNumber { get; set; }
    public Guid? MessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CaseActionResponse
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string Status { get; set; } = CaseActionStatus.Pending;
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Result { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public Guid? MessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CaseParticipantResponse
{
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Messages { get; set; }
    public DateTimeOffset LastAt { get; set; }
}

public class CasePlanNoteResponse
{
    public int Number { get; set; }
    public string PlanPhase { get; set; } = ExecutionPhase.Analysis;
    public string? StepKey { get; set; }
    public string Text { get; set; } = string.Empty;
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public bool FromExecutor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<string> Attachments { get; set; } = [];
}

public static class CaseChatMappings
{
    public static CaseMessageResponse ToResponse(this CaseMessage m, IEnumerable<CaseFile>? files = null) => new()
    {
        Id = m.Id,
        Number = m.Number,
        AuthorKind = m.AuthorKind,
        AuthorUserId = m.AuthorUserId,
        AuthorName = m.AuthorName,
        Kind = m.Kind,
        Text = m.Text,
        Transcript = m.Transcript,
        PeopleOnly = m.PeopleOnly,
        Deep = m.Deep,
        Mentions = m.Mentions.ToList(),
        Steps = m.Steps.ToList(),
        ReplyToNumber = m.ReplyToNumber,
        ProposalId = m.ProposalId,
        ActionId = m.ActionId,
        Model = m.Model,
        InputTokens = m.InputTokens,
        OutputTokens = m.OutputTokens,
        DurationMs = m.DurationMs,
        CreatedAt = m.CreatedAt,
        EditedAt = m.EditedAt,
        Files = (files ?? []).Select(f => f.ToResponse()).ToList()
    };

    public static CaseFileResponse ToResponse(this CaseFile f) => new()
    {
        Id = f.Id,
        Number = f.Number,
        MessageId = f.MessageId,
        Name = f.Name,
        Kind = f.Kind,
        ContentType = f.ContentType,
        Size = f.Size,
        CreatedBy = f.CreatedBy,
        FromSpecialist = f.FromSpecialist,
        Hidden = f.Hidden,
        CreatedAt = f.CreatedAt
    };

    public static CasePointResponse ToResponse(this CasePoint p) => new()
    {
        Id = p.Id,
        Text = p.Text,
        Status = p.Status,
        MessageNumber = p.MessageNumber,
        CreatedBy = p.CreatedBy,
        FromSpecialist = p.FromSpecialist,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };

    public static CaseProposalResponse ToResponse(this CaseProposal p) => new()
    {
        Id = p.Id,
        Number = p.Number,
        Title = p.Title,
        Summary = p.Summary,
        Phase = p.Phase,
        Changes = p.Changes.Select(c => new CaseProposalChange { Kind = c.Kind, Title = c.Title, Detail = c.Detail, FromMessage = c.FromMessage }).ToList(),
        Status = p.Status,
        Execute = p.Execute,
        DecidedBy = p.DecidedBy,
        DecidedAt = p.DecidedAt,
        RejectReason = p.RejectReason,
        PlanNoteNumber = p.PlanNoteNumber,
        MessageId = p.MessageId,
        CreatedAt = p.CreatedAt
    };

    public static CaseActionResponse ToResponse(this CaseAction a) => new()
    {
        Id = a.Id,
        Number = a.Number,
        Title = a.Title,
        Detail = a.Detail,
        Status = a.Status,
        DecidedBy = a.DecidedBy,
        DecidedAt = a.DecidedAt,
        Result = a.Result,
        DoneAt = a.DoneAt,
        MessageId = a.MessageId,
        CreatedAt = a.CreatedAt
    };
}
