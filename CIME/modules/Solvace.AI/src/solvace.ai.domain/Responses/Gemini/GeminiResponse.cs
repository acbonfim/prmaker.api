using System.Text.Json.Serialization;

namespace solvace.ai.domain.Responses.Gemini;
public class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }
}