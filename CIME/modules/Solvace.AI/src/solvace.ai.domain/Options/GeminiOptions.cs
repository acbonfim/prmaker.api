namespace solvace.ai.domain.Options;

public class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
    public string Model { get; set; } = "gemini-2.5-flash";
}