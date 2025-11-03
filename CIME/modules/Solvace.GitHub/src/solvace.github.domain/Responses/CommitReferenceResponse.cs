namespace solvace.github.domain.Responses;

public class CommitReferenceResponse
{
    public string Sha { get; set; } = string.Empty;
    public string[] Parents { get; set; } = Array.Empty<string>();
    public string Message { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}