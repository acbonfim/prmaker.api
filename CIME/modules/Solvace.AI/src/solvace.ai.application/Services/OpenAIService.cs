using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using solvace.ai.application.Contract;
using solvace.ai.domain.Options;
using solvace.ai.domain.Responses;
using solvace.ai.domain.Requests.OpenAI;
using solvace.ai.domain.Responses.OpenAI;

namespace solvace.ai.application.Services;

public class OpenAIService : IAIService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAIOptions _options;

    public OpenAIService(IHttpClientFactory httpClientFactory, IOptions<AIOptions> aiOptions)
    {
        _httpClientFactory = httpClientFactory;
        _options = aiOptions.Value.OpenAI ?? throw new InvalidOperationException("Configurações do OpenAI não encontradas");

        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("ApiKey do OpenAI não configurado");
    }

    public async Task<AIGenerateResponse?> GenerateContentAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return new AIGenerateResponse { Error = "Prompt não pode ser vazio" };

        var requestBody = new OpenAIRequest
        {
            Model = _options.Model,
            Messages = new List<OpenAIMessageRequest>
            {
                new OpenAIMessageRequest
                {
                    Role = "user",
                    Content = prompt
                }
            }
        };

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");

        try
        {
            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return new AIGenerateResponse
                {
                    Error = $"Erro ao gerar conteúdo no OpenAI: {response.StatusCode}",
                    Provider = "OpenAI",
                    Model = _options.Model
                };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(content);

            var text = openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content;
            var tokensUsed = openAIResponse?.Usage?.TotalTokens;

            return new AIGenerateResponse
            {
                Content = text,
                Provider = "OpenAI",
                Model = _options.Model,
                TokensUsed = tokensUsed
            };
        }
        catch (Exception ex)
        {
            return new AIGenerateResponse
            {
                Error = $"Erro ao comunicar com OpenAI: {ex.Message}",
                Provider = "OpenAI",
                Model = _options.Model
            };
        }
    }
}