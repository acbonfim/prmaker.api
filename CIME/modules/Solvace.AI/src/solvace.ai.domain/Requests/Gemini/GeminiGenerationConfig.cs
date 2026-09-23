using System.Text.Json.Serialization;

namespace solvace.ai.domain.Requests.Gemini;

public class GeminiGenerationConfig
{
    // Atenção: nos modelos Flash 3.x (ex.: gemini-3.6-flash) os parâmetros de sampling
    // (temperature, topP, topK) são IGNORADOS pela API — não causam erro, mas não têm efeito.
    // Para controle de precisão/estilo use o System Instruction. Mantidos aqui por
    // compatibilidade com modelos que ainda os honram.
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("topP")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    [JsonPropertyName("topK")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TopK { get; set; }

    [JsonPropertyName("maxOutputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("candidateCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CandidateCount { get; set; }

    [JsonPropertyName("thinkingConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiThinkingConfig? ThinkingConfig { get; set; }
}
