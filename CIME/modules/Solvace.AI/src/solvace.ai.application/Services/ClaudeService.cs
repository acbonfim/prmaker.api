using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using solvace.ai.application.Contract;
using solvace.ai.domain.Options;
using solvace.ai.domain.Responses;
using solvace.ai.domain.Requests;
namespace solvace.ai.application.Services;

public class ClaudeService : IAIService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ClaudeOptions _options;

    public ClaudeService(IHttpClientFactory httpClientFactory, IOptions<AIOptions> aiOptions)
    {
        _httpClientFactory = httpClientFactory;
        _options = aiOptions.Value.Claude ?? throw new InvalidOperationException("Configurações do Claude não encontradas");
    }

    public async Task<AIGenerateResponse?> GenerateContentAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // TODO: Implementar integração com Claude
        return await Task.FromResult(new AIGenerateResponse { Error = "Claude ainda não implementado", Provider = "Claude" });
        // return new AIGenerateResponse { Error = "Claude ainda não implementado", Provider = "Claude" };
    }
}