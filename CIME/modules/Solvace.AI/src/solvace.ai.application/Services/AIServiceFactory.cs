using System.Text.Json;
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
    private readonly IPluginCacheManager _pluginCacheManager;
    private readonly AIOptions _options;

    public AIServiceFactory(IHttpClientFactory httpClientFactory, IOptions<AIOptions> options, IPluginCacheManager pluginCacheManager)
    {
        _httpClientFactory = httpClientFactory;
        _pluginCacheManager = pluginCacheManager;
        _options = options.Value;
    }

    public IAIService CreateService(string providerName, Plugin plugin)
    {
        var providerOptions = new
        {
            ApiKey = plugin.Configurations.GetConfigurationValue("ApiKey"),
            BaseUrl = plugin.Configurations.GetConfigurationValue("BaseUrl"),
            Model = plugin.Configurations.GetConfigurationValue("Model"),
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
            var typedOptions = JsonSerializer.Deserialize(json, property.PropertyType);
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
}







