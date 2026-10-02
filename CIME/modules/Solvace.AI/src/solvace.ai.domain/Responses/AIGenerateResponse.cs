namespace solvace.ai.domain.Responses;

public class AIGenerateResponse
{
    public string? Content { get; set; }
    public string? Error { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int? TokensUsed { get; set; }
    /// <summary>Tokens de entrada (prompt) e de saída (resposta) — para o custo por ação (0042).</summary>
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
}

