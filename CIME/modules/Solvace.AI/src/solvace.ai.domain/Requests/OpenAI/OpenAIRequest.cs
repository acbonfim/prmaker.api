using System.Text.Json.Serialization;

namespace solvace.ai.domain.Requests.OpenAI;

public class OpenAIRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAIMessageRequest> Messages { get; set; } = new();

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; } = 0.7;

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }
}