namespace solvace.github.domain.Responses;

public class CommitFileDiffResponse
{
    public string Filename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public int Changes { get; set; }
    public string? Patch { get; set; }
    public string? BlobUrl { get; set; }
    public string? RawUrl { get; set; }
    public string? ContentsUrl { get; set; }
}