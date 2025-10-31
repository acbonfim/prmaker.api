namespace solvace.github.domain.Responses;

public class CompareDiffResponse
{
    public string? Url { get; set; }
    public string? HtmlUrl { get; set; }
    public string? PermalinkUrl { get; set; }
    public int TotalCommits { get; set; }
    public int AheadBy { get; set; }
    public int BehindBy { get; set; }
    public string Status { get; set; } = string.Empty;
    public CommitFileDiffResponse[] Files { get; set; } = Array.Empty<CommitFileDiffResponse>();
}