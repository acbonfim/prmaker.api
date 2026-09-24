using System.Text.Json;
using System.Text.Json.Serialization;
using Cime.BuildingBlocks.GlobalModels;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using solvace.ai.application.Contract;
using solvace.ai.application.Services;
using solvace.ai.domain.Options;
using solvace.ai.domain.Responses;
using solvace.ai.domain.Requests.OpenAI;
using solvace.ai.domain.Responses.OpenAI;
using solvace.prform.application;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;


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

    /// <summary>
    /// Cria o provedor com a configuração EFETIVA do plugin "{Provider} Plugin" — global, ou a do
    /// usuário quando o plugin é de uso pessoal (feature 0002; ver PluginAIService).
    /// </summary>
    public IAIService CreateService(string providerName, PluginConfiguration configuration)
    {
        var providerOptions = new
        {
            ApiKey = configuration.GetConfigurationValue("ApiKey"),
            BaseUrl = configuration.GetConfigurationValue("BaseUrl"),
            Model = configuration.GetConfigurationValue("Model"),
            // Parâmetros opcionais específicos do Gemini 3.x (ignorados pelos demais providers,
            // que não possuem essas propriedades no seu option type).
            SystemInstruction = NullIfEmpty(configuration.GetConfigurationValue("SystemInstruction")),
            ThinkingLevel = NullIfEmpty(configuration.GetConfigurationValue("ThinkingLevel")),
            MaxOutputTokens = NullIfEmpty(configuration.GetConfigurationValue("MaxOutputTokens")),
            TimeoutSeconds = NullIfEmpty(configuration.GetConfigurationValue("TimeoutSeconds")),
            // Específicos do Claude (ignorados pelos demais providers).
            MaxRetries = NullIfEmpty(configuration.GetConfigurationValue("MaxRetries")),
            Effort = NullIfEmpty(configuration.GetConfigurationValue("Effort")),
        };

        var aiOptions = new AIOptions
        {
            Provider = providerName
        };

        var propertyName = char.ToUpper(providerName[0]) + providerName.Substring(1).ToLower();
        var property = typeof(AIOptions).GetProperty(propertyName);

        if (property != null)
        {
            var json = JsonSerializer.Serialize(providerOptions);
            // AllowReadingFromString: converte valores numéricos (ex.: MaxOutputTokens) que vêm
            // como string das configurações do plugin salvas no banco.
            var deserializeOptions = new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
            var typedOptions = JsonSerializer.Deserialize(json, property.PropertyType, deserializeOptions);
            property.SetValue(aiOptions, typedOptions);
        }
        
        return providerName.ToLowerInvariant() switch
        {
            "gemini" => new GeminiService(_httpClientFactory, Options.Create(aiOptions)),
            "openai" => new OpenAIService(_httpClientFactory, Options.Create(aiOptions)),
            "claude" => new ClaudeService(_httpClientFactory, Options.Create(aiOptions)),
            "copilot" => new CopilotService(_httpClientFactory, Options.Create(aiOptions)),
            _ => throw new InvalidOperationException($"Provider '{providerName}' não é suportado. Use: Gemini, OpenAI, Claude ou Copilot")
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}







