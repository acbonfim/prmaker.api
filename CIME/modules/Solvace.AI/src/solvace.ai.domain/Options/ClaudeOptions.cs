namespace solvace.ai.domain.Options;

public class ClaudeOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
    public string Model { get; set; } = "claude-3-5-sonnet-20241022";
}