using System.Text.Json.Serialization;

namespace solvace.ai.domain.Responses.Gemini;

/// <summary>
/// Corpo de erro estruturado retornado pela API do Gemini em respostas não-2xx.
/// Formato: { "error": { "code": 400, "message": "...", "status": "INVALID_ARGUMENT" } }
/// </summary>
public class GeminiErrorResponse
{
    [JsonPropertyName("error")]
    public GeminiError? Error { get; set; }
}

public class GeminiError
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
