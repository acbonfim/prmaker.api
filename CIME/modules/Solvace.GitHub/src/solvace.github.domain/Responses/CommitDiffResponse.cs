namespace solvace.github.domain.Responses;

public class CommitDiffResponse
{
    public string Sha { get; set; } = string.Empty;
    public string[] Parents { get; set; } = Array.Empty<string>();
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public int Total { get; set; }
    public CommitFileDiffResponse[] Files { get; set; } = Array.Empty<CommitFileDiffResponse>();
    public string Error { get; set; } = string.Empty;
}