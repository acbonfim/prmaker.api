namespace solvace.github.domain.Responses;

public class BranchReferenceResponse
{
    public string Name { get; set; } = string.Empty;
    public bool Protected { get; set; }
    public string CommitSha { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}