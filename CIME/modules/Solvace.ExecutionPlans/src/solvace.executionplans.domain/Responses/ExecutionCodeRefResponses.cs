using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>0075: cabeça da ref (lista do card, sem o código) — o suficiente para ligar citações e pintar o link.</summary>
public class ExecutionCodeRefHeadResponse
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string Ref { get; set; } = string.Empty;
    public string? Repository { get; set; }
    public string Path { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Change { get; set; } = ExecutionCodeChange.Read;
    public string? Title { get; set; }
    public string? CommitSha { get; set; }
    public bool HasProposal { get; set; }
    public bool HasImplemented { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>0075: ref completa (o popover).</summary>
public class ExecutionCodeRefResponse : ExecutionCodeRefHeadResponse
{
    public string CardNumber { get; set; } = string.Empty;
    public string? Branch { get; set; }
    public string? Language { get; set; }
    public string? Why { get; set; }
    public string? Snapshot { get; set; }
    public int FirstLine { get; set; } = 1;
    public string? Proposal { get; set; }
    public string? Implemented { get; set; }
    public string? ImplementedSha { get; set; }
    public List<ExecutionCodeAnnotation> Annotations { get; set; } = [];
    public string CreatedBy { get; set; } = string.Empty;
}

public static class ExecutionCodeRefMappings
{
    public static ExecutionCodeRefResponse ToResponse(this ExecutionCodeRef r) => new()
    {
        Id = r.Id,
        PlanId = r.PlanId,
        CardNumber = r.CardNumber,
        Ref = r.Ref,
        Repository = r.Repository,
        Path = r.Path,
        StartLine = r.StartLine,
        EndLine = r.EndLine,
        Change = r.Change,
        Title = r.Title,
        CommitSha = r.CommitSha,
        Branch = r.Branch,
        Language = r.Language,
        Why = r.Why,
        Snapshot = r.Snapshot,
        FirstLine = r.FirstLine,
        Proposal = r.Proposal,
        Implemented = r.Implemented,
        ImplementedSha = r.ImplementedSha,
        HasProposal = r.Proposal is not null,
        HasImplemented = r.Implemented is not null,
        Annotations = r.Annotations,
        CreatedBy = r.CreatedBy,
        UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
    };
}
