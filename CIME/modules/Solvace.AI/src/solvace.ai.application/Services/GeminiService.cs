using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using solvace.ai.application.Contract;
using solvace.ai.domain.Options;
using solvace.ai.domain.Responses;
using solvace.ai.domain.Requests.Gemini;
using solvace.ai.domain.Responses.Gemini;

namespace solvace.ai.application.Services;

public class GeminiService : IAIService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiOptions _options;

    public GeminiService(IHttpClientFactory httpClientFactory, IOptions<AIOptions> aiOptions)
    {
        _httpClientFactory = httpClientFactory;
        _options = aiOptions.Value.Gemini ?? throw new InvalidOperationException("Configurações do Gemini não encontradas");

        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("ApiKey do Gemini não configurado");
    }

    public async Task<AIGenerateResponse?> GenerateContentAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return new AIGenerateResponse { Error = "Prompt não pode ser vazio" };

        var requestBody = new GeminiRequest
        {
            Contents = new List<GeminiContent>
            {
                new GeminiContent
                {
                    Parts = new List<GeminiPart>
                    {
                        new GeminiPart { Text = prompt }
                    }
                }
            }
        };

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("x-goog-api-key", _options.ApiKey);

        try
        {
            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return new AIGenerateResponse
                {
                    Error = $"Erro ao gerar conteúdo no Gemini: {response.StatusCode}",
                    Provider = "Gemini",
                    Model = _options.Model
                };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(content);

            var text = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

            return new AIGenerateResponse
            {
                Content = text,
                Provider = "Gemini",
                Model = _options.Model
            };
        }
        catch (Exception ex)
        {
            return new AIGenerateResponse
            {
                Error = $"Erro ao comunicar com Gemini: {ex.Message}",
                Provider = "Gemini",
                Model = _options.Model
            };
        }
    }
}

