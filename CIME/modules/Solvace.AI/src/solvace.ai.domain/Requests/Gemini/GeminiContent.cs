using System.Text.Json.Serialization;

namespace solvace.ai.domain.Requests.Gemini;

public class GeminiContent
{
    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; set; } = new();
}