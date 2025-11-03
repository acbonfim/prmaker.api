namespace solvace.github.domain.Responses;

public class PullRequestResponse
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string ClosedAt { get; set; } = string.Empty;
    public string MergedAt { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string AuthorAvatarUrl { get; set; } = string.Empty;
    public string AuthorUrl { get; set; } = string.Empty;
    public string AuthorHtmlUrl { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Head { get; set; } = string.Empty;
    public string Base { get; set; } = string.Empty;
    public bool IsDraft { get; set; }
    public string Error { get; set; } = string.Empty;

}