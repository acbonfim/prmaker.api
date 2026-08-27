using System.Text.Json.Serialization;

namespace solvace.ai.domain.Requests.Gemini;

public class GeminiPart
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

