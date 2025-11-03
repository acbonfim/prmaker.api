using System.Text.Json.Serialization;

namespace solvace.ai.domain.Responses.OpenAI;

public class OpenAIMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

