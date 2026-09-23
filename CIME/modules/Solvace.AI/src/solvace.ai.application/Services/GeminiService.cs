using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using solvace.ai.application.Contract;
using solvace.ai.domain.Options;
using solvace.ai.domain.Responses;
using solvace.ai.domain.Requests.Gemini;
using solvace.ai.domain.Responses.Gemini;

namespace solvace.ai.application.Services;

public class GeminiService : IAIService
{
    private const string ProviderName = "Gemini";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
            return Failure("Prompt não pode ser vazio");

        var requestBody = BuildRequest(prompt);

        var client = _httpClientFactory.CreateClient();
        if (_options.TimeoutSeconds is > 0)
            client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody, SerializerOptions),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Add("x-goog-api-key", _options.ApiKey);

        try
        {
            var response = await client.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Failure(FormatApiError(response.StatusCode, content));

            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(content, SerializerOptions);

            // Prompt bloqueado por políticas de segurança — nenhum candidato é retornado.
            var blockReason = geminiResponse?.PromptFeedback?.BlockReason;
            if (!string.IsNullOrEmpty(blockReason))
                return Failure($"Prompt bloqueado pelo Gemini (blockReason: {blockReason})");

            var candidate = geminiResponse?.Candidates?.FirstOrDefault();
            var text = candidate?.Content?.Parts?.FirstOrDefault()?.Text;

            // Geração interrompida sem texto útil (segurança, limite de tokens, recitação, etc.).
            if (string.IsNullOrEmpty(text))
            {
                var finishReason = candidate?.FinishReason;
                var detail = string.IsNullOrEmpty(finishReason) ? "resposta vazia" : $"finishReason: {finishReason}";
                return Failure($"Gemini não retornou conteúdo ({detail})");
            }

            return new AIGenerateResponse
            {
                Content = text,
                Provider = ProviderName,
                Model = _options.Model,
                TokensUsed = geminiResponse?.UsageMetadata?.TotalTokenCount
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelamento explícito pelo chamador (ex.: request abortada pelo cliente).
            return Failure("Requisição ao Gemini cancelada pelo chamador");
        }
        catch (OperationCanceledException ex)
        {
            // TaskCanceledException sem cancelamento do token == timeout do HttpClient.
            var timeout = _options.TimeoutSeconds is > 0 ? _options.TimeoutSeconds.Value : 100;
            return Failure($"Timeout ({timeout}s) ao aguardar resposta do Gemini. Detalhe: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            var status = ex.StatusCode is not null ? $" (HTTP {(int)ex.StatusCode})" : string.Empty;
            return Failure($"Falha de conexão com o Gemini{status}: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return Failure($"Resposta do Gemini em formato inesperado: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Failure($"Erro ao comunicar com Gemini: {ex.Message}");
        }
    }

    /// <summary>
    /// Extrai a mensagem legível do corpo de erro do Gemini
    /// ({ "error": { "code", "message", "status" } }), com fallback para o corpo cru.
    /// </summary>
    private static string FormatApiError(System.Net.HttpStatusCode statusCode, string content)
    {
        var prefix = $"Erro HTTP {(int)statusCode} do Gemini";

        if (string.IsNullOrWhiteSpace(content))
            return $"{prefix} (sem corpo de resposta)";

        try
        {
            var error = JsonSerializer.Deserialize<GeminiErrorResponse>(content, SerializerOptions)?.Error;
            if (error is not null && !string.IsNullOrWhiteSpace(error.Message))
            {
                var status = string.IsNullOrWhiteSpace(error.Status) ? string.Empty : $" [{error.Status}]";
                return $"{prefix}{status}: {error.Message}";
            }
        }
        catch (JsonException)
        {
            // Corpo não é o JSON de erro esperado — cai no fallback abaixo.
        }

        return $"{prefix}: {content}";
    }

    private GeminiRequest BuildRequest(string prompt)
    {
        var request = new GeminiRequest
        {
            Contents = new List<GeminiContent>
            {
                // role "user" explícito. Nunca enviar role "model" (prefill de resposta foi
                // removido nos modelos 3.x e causa erro HTTP 400).
                new GeminiContent
                {
                    Role = "user",
                    Parts = new List<GeminiPart> { new GeminiPart { Text = prompt } }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(_options.SystemInstruction))
        {
            request.SystemInstruction = new GeminiContent
            {
                Parts = new List<GeminiPart> { new GeminiPart { Text = _options.SystemInstruction } }
            };
        }

        var generationConfig = BuildGenerationConfig();
        if (generationConfig is not null)
            request.GenerationConfig = generationConfig;

        return request;
    }

    private GeminiGenerationConfig? BuildGenerationConfig()
    {
        GeminiGenerationConfig? config = null;

        if (_options.MaxOutputTokens is > 0)
            config = new GeminiGenerationConfig { MaxOutputTokens = _options.MaxOutputTokens };

        if (!string.IsNullOrWhiteSpace(_options.ThinkingLevel))
        {
            config ??= new GeminiGenerationConfig();
            // Substitui thinking_budget (descontinuado). Enviar ambos causaria erro 400.
            config.ThinkingConfig = new GeminiThinkingConfig { ThinkingLevel = _options.ThinkingLevel };
        }

        return config;
    }

    private AIGenerateResponse Failure(string error) => new()
    {
        Error = error,
        Provider = ProviderName,
        Model = _options.Model
    };
}
