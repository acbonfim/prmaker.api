namespace solvace.prform.domain.Responses;

public class PullRequestRegisterResponse
{
    public int Id { get; set; }
    public string CardNumber { get; set; }
    public string RootCause { get; set; }
    public string Description { get; set; }
    public string BranchPrefix { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;

}