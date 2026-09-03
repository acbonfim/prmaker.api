using System.Text.Json.Serialization;

namespace solvace.ai.domain.Responses.Gemini;

public class GeminiPromptFeedback
{
    [JsonPropertyName("blockReason")]
    public string? BlockReason { get; set; }
}
