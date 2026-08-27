using System.Text.Json.Serialization;

namespace solvace.ai.domain.Requests.OpenAI;

public class OpenAIMessageRequest
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user"; // "user", "assistant", "system"

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}
