using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Requests;

/// <summary>0075: fotografia de um trecho de código (o <c>prmake-plan.sh coderef</c> monta a partir do arquivo local).</summary>
public class SaveExecutionCodeRefRequest
{
    public string? Repository { get; set; }
    public string? Path { get; set; }
    public string? CommitSha { get; set; }
    public string? Branch { get; set; }
    public string? Language { get; set; }
    public int StartLine { get; set; }
    public int? EndLine { get; set; }
    public int? FirstLine { get; set; }
    public string? Snapshot { get; set; }
    public string? Change { get; set; }
    public string? Title { get; set; }
    public string? Why { get; set; }
    public string? Proposal { get; set; }
    public List<ExecutionCodeAnnotation>? Annotations { get; set; }

    public ExecutionCodeCapture ToCapture() => new(Repository, Path, CommitSha, Branch, Language, StartLine, EndLine, FirstLine,
        Snapshot, Change, Title, Why, Proposal, Annotations);
}

/// <summary>0075: ajuste parcial (campo ausente = não muda; texto vazio limpa).</summary>
public class PatchExecutionCodeRefRequest
{
    public string? Change { get; set; }
    public string? Title { get; set; }
    public string? Why { get; set; }
    public string? Proposal { get; set; }
    public List<ExecutionCodeAnnotation>? Annotations { get; set; }
    public string? Implemented { get; set; }
    public string? ImplementedSha { get; set; }

    public ExecutionCodeRefPatch ToPatch() => new(Change, Title, Why, Proposal, Annotations, Implemented, ImplementedSha);
}
