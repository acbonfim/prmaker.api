using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using solvace.ai.application.Contract;
using solvace.ai.domain.Options;
using solvace.ai.domain.Responses;
using solvace.ai.domain.Requests;

namespace solvace.ai.application.Services;

public class CopilotService : IAIService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CopilotOptions _options;

    public CopilotService(IHttpClientFactory httpClientFactory, IOptions<AIOptions> aiOptions)
    {
        _httpClientFactory = httpClientFactory;
        _options = aiOptions.Value.Copilot ?? throw new InvalidOperationException("Configurações do Copilot não encontradas");
    }

    public async Task<AIGenerateResponse?> GenerateContentAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // TODO: Implementar integração com Copilot
        return await Task.FromResult(new AIGenerateResponse { Error = "Copilot ainda não implementado", Provider = "Copilot" });
        // return new AIGenerateResponse { Error = "Copilot ainda não implementado", Provider = "Copilot" };
    }
}