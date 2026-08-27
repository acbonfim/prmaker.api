namespace solvace.ai.domain.Requests;

public class AIGenerateRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
}

