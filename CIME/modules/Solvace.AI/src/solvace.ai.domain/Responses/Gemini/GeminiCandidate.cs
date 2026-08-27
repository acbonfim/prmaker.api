using System.Text.Json.Serialization;
using solvace.ai.domain.Requests.Gemini;

namespace solvace.ai.domain.Responses.Gemini;
public class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }
}