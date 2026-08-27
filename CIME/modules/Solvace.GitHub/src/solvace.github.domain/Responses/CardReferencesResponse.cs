namespace solvace.github.domain.Responses;

public class CardReferencesResponse
{
    public string CardNumber { get; set; } = string.Empty;
    public string[] Patterns { get; set; } = Array.Empty<string>();
    public BranchReferenceResponse[] Branches { get; set; } = Array.Empty<BranchReferenceResponse>();
    public PullRequestReferenceResponse[] PullRequests { get; set; } = Array.Empty<PullRequestReferenceResponse>();
    public CommitReferenceResponse[] Commits { get; set; } = Array.Empty<CommitReferenceResponse>();
    public CodeHitReferenceResponse[] CodeHits { get; set; } = Array.Empty<CodeHitReferenceResponse>();
    public LimitsResponse Limits { get; set; } = new LimitsResponse();
    public string Error { get; set; } = string.Empty;
}