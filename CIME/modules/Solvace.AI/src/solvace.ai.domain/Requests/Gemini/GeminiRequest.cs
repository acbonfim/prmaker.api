using System.Text.Json.Serialization;

namespace solvace.ai.domain.Requests.Gemini;

public class GeminiRequest
{
    [JsonPropertyName("contents")]
    public List<GeminiContent> Contents { get; set; } = new();
}