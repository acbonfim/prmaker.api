using System.Text.Json.Serialization;

namespace solvace.ai.domain.Requests.Gemini;

/// <summary>
/// Configuração de "pensamento" (thinking) dos modelos Gemini 3.x.
/// Substitui o parâmetro legado thinking_budget (descontinuado).
/// Valores aceitos para <see cref="ThinkingLevel"/>: minimal, low, medium, high.
/// </summary>
public class GeminiThinkingConfig
{
    [JsonPropertyName("thinkingLevel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThinkingLevel { get; set; }
}
