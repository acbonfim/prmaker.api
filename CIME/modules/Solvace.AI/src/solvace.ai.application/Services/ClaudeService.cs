using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using solvace.ai.application.Contract;
using solvace.ai.domain.Options;
using solvace.ai.domain.Responses;

namespace solvace.ai.application.Services;

/// <summary>
/// Provedor Claude (Anthropic Messages API) pelo SDK oficial. Gera texto a partir do prompt
/// pronto: sem histórico, sem ferramentas. Retentativas de 429/5xx/529 ficam por conta do SDK.
/// </summary>
public class ClaudeService : IAIService
{
    private const string ProviderName = "Claude";
    public const string HttpClientName = "Anthropic";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ClaudeOptions _options;

    public ClaudeService(IHttpClientFactory httpClientFactory, IOptions<AIOptions> aiOptions)
    {
        _httpClientFactory = httpClientFactory;
        _options = aiOptions.Value.Claude ?? new ClaudeOptions();
    }

    public async Task<AIGenerateResponse?> GenerateContentAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return Failure("Prompt não pode ser vazio");
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return Failure("ApiKey do Claude não configurada — preencha em Minhas integrações");

        var model = string.IsNullOrWhiteSpace(_options.Model) ? ClaudeOptions.DefaultModel : _options.Model.Trim();

        if (!TryParseEffort(_options.Effort, out var effort))
            return Failure($"Effort inválido no plugin do Claude: '{_options.Effort}' (use low, medium, high ou max)", model);

        var timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds is > 0 ? _options.TimeoutSeconds.Value : ClaudeOptions.DefaultTimeoutSeconds);

        // HttpClient do IHttpClientFactory (conexões reaproveitadas); o cliente do SDK é leve e
        // criado por requisição porque a chave pode ser de cada usuário (integração pessoal).
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = timeout + TimeSpan.FromSeconds(10); // rede de segurança; o SDK controla o timeout por tentativa

        var client = new AnthropicClient
        {
            ApiKey = _options.ApiKey.Trim(),
            BaseUrl = NormalizeBaseUrl(_options.BaseUrl),
            HttpClient = httpClient,
            MaxRetries = _options.MaxRetries is >= 0 ? _options.MaxRetries.Value : ClaudeOptions.DefaultMaxRetries,
            Timeout = timeout,
        };

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = _options.MaxOutputTokens is > 0 ? _options.MaxOutputTokens.Value : ClaudeOptions.DefaultMaxOutputTokens,
            // null já no tipo do SDK (não passa pela conversão implícita de string): campo omitido.
            System = string.IsNullOrWhiteSpace(_options.SystemInstruction)
                ? (MessageCreateParamsSystem?)null
                : _options.SystemInstruction.Trim(),
            OutputConfig = effort is null ? null : new OutputConfig { Effort = effort.Value },
            // Cabeçalho anthropic-workspace-id: só para chaves de organização (sem workspace).
            WorkspaceID = string.IsNullOrWhiteSpace(_options.WorkspaceId) ? null : _options.WorkspaceId.Trim(),
            // Sem temperature/top_p: os modelos atuais (Sonnet 5, Opus 5…) rejeitam amostragem manual.
            Messages = [new() { Role = Role.User, Content = prompt }],
        };

        Message message;
        try
        {
            message = await client.Messages.Create(parameters, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure("Requisição ao Claude cancelada pelo chamador", model);
        }
        catch (AnthropicUnauthorizedException)
        {
            return Failure("ApiKey do Claude inválida ou revogada — confira em Minhas integrações", model);
        }
        catch (AnthropicForbiddenException e)
        {
            return Failure($"A ApiKey do Claude não tem permissão para esta operação: {e.Message}", model);
        }
        catch (AnthropicNotFoundException)
        {
            return Failure($"Modelo '{model}' não encontrado na API da Anthropic — confira o campo Model do plugin", model);
        }
        catch (AnthropicBadRequestException e) when (e.Message.Contains("anthropic-workspace-id", StringComparison.OrdinalIgnoreCase))
        {
            return Failure("Esta ApiKey do Claude não pertence a um workspace da Anthropic: informe o WorkspaceId (wrkspc_...) no plugin " +
                           "ou use uma chave criada dentro de um workspace (console.anthropic.com > Workspaces)", model);
        }
        catch (AnthropicBadRequestException e)
        {
            return Failure($"Requisição recusada pela API do Claude (confira Model/Effort/MaxOutputTokens no plugin): {e.Message}", model);
        }
        catch (AnthropicRateLimitException)
        {
            return Failure("Limite de uso da API do Claude atingido (mesmo após novas tentativas). Tente de novo em instantes", model);
        }
        catch (Anthropic5xxException)
        {
            // Inclui 529 (overloaded): o SDK já tentou de novo com backoff.
            return Failure("A API do Claude está sobrecarregada ou indisponível no momento (mesmo após novas tentativas). Tente de novo em instantes", model);
        }
        catch (AnthropicIOException e)
        {
            return Failure($"Falha de conexão com a API do Claude: {e.Message}", model);
        }
        catch (AnthropicApiException e)
        {
            return Failure($"Erro na API do Claude: {e.Message}", model);
        }
        catch (OperationCanceledException)
        {
            return Failure($"Timeout ({timeout.TotalSeconds:0}s) ao aguardar resposta do Claude", model);
        }

        var text = string.Concat(message.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        var tokens = (int)Math.Min(int.MaxValue, message.Usage.InputTokens + message.Usage.OutputTokens);

        if (message.StopReason == "refusal")
            return Failure("O Claude recusou gerar este conteúdo", model, tokens);
        if (message.StopReason == "max_tokens")
            return Failure($"A resposta do Claude foi cortada no limite de {parameters.MaxTokens} tokens — aumente MaxOutputTokens no plugin", model, tokens);
        if (string.IsNullOrWhiteSpace(text))
            return Failure($"O Claude não retornou texto (stop_reason: {message.StopReason})", model, tokens);

        return new AIGenerateResponse
        {
            Content = text,
            Provider = ProviderName,
            Model = model,
            TokensUsed = tokens,
        };
    }

    /// <summary>Raiz da API: aceita vazio (padrão) e o valor antigo com "/v1/messages".</summary>
    private static string NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return ClaudeOptions.DefaultBaseUrl;

        var url = baseUrl.Trim().TrimEnd('/');
        foreach (var suffix in new[] { "/v1/messages", "/v1" })
            if (url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                url = url[..^suffix.Length];
        return url;
    }

    private static bool TryParseEffort(string? value, out Effort? effort)
    {
        effort = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        effort = value.Trim().ToLowerInvariant() switch
        {
            "low" => Effort.Low,
            "medium" => Effort.Medium,
            "high" => Effort.High,
            "max" => Effort.Max,
            _ => null,
        };
        return effort is not null;
    }

    private static AIGenerateResponse Failure(string error, string? model = null, int? tokens = null) =>
        new() { Error = error, Provider = ProviderName, Model = model, TokensUsed = tokens };
}
