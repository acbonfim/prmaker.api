using System.Text.Json.Serialization;

namespace solvace.ai.domain.Responses.OpenAI;
public class OpenAIError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}