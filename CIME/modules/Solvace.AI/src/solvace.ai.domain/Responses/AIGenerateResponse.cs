namespace solvace.ai.domain.Responses;

public class AIGenerateResponse
{
    public string? Content { get; set; }
    public string? Error { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int? TokensUsed { get; set; }
}

