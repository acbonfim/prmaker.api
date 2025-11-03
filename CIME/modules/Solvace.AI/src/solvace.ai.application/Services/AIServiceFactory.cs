using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using solvace.ai.application.Contract;
using solvace.ai.application.Services;
using solvace.ai.domain.Options;
using solvace.ai.domain.Responses;
using solvace.ai.domain.Requests.OpenAI;
using solvace.ai.domain.Responses.OpenAI;


namespace solvace.ai.application.Services;

public class AIServiceFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AIOptions _options;

    public AIServiceFactory(IHttpClientFactory httpClientFactory, IOptions<AIOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public IAIService CreateService()
    {
        return _options.Provider.ToLowerInvariant() switch
        {
            "gemini" => new GeminiService(_httpClientFactory, Options.Create(_options)),
            "openai" => new OpenAIService(_httpClientFactory, Options.Create(_options)),
            "claude" => new ClaudeService(_httpClientFactory, Options.Create(_options)),
            "copilot" => new CopilotService(_httpClientFactory, Options.Create(_options)),
            _ => throw new InvalidOperationException($"Provider '{_options.Provider}' não é suportado. Use: Gemini, OpenAI, Claude ou Copilot")
        };
    }
}







