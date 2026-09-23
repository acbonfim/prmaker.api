namespace solvace.ai.domain.Options;

public class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;

    // Endpoint generateContent (classificado como legado desde jun/2026 — a nova interface
    // padrão é a Interactions API. Mantido por ser a integração atual em produção).
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent";

    public string Model { get; set; } = "gemini-3.6-flash";

    /// <summary>
    /// Instrução de sistema opcional. Nos modelos Flash 3.x é o lugar correto para
    /// diretrizes de precisão/estilo (os parâmetros de sampling são ignorados).
    /// </summary>
    public string? SystemInstruction { get; set; }

    /// <summary>
    /// Nível de "pensamento" (thinking) — minimal, low, medium ou high.
    /// Substitui o parâmetro legado thinking_budget.
    /// </summary>
    public string? ThinkingLevel { get; set; }

    /// <summary>
    /// Limite máximo de tokens de saída (opcional). Ainda respeitado pelos modelos 3.x.
    /// </summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// Timeout da requisição HTTP em segundos. Se não configurado, usa o padrão do HttpClient (100s).
    /// Gerações com ThinkingLevel alto ou saídas longas podem exigir um valor maior.
    /// </summary>
    public int? TimeoutSeconds { get; set; }
}
