namespace solvace.ai.domain.Options;

public class AIOptions
{
    public const string SectionName = "AI";

    public string Provider { get; set; } = "Gemini"; // Gemini, OpenAI, Claude, Copilot
    public GeminiOptions? Gemini { get; set; }
    public OpenAIOptions? OpenAI { get; set; }
    public ClaudeOptions? Claude { get; set; }
    public CopilotOptions? Copilot { get; set; }
}