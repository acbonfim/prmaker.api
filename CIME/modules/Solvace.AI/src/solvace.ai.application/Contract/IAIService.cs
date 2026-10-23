using solvace.ai.domain.Requests;
using solvace.ai.domain.Responses;

namespace solvace.ai.application.Contract;

public interface IAIService
{
    Task<AIGenerateResponse?> GenerateContentAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>0071: com ajustes só desta chamada; provedor que não os usa ignora.</summary>
    Task<AIGenerateResponse?> GenerateContentAsync(string prompt, AIGenerateOptions? options, CancellationToken cancellationToken = default) =>
        GenerateContentAsync(prompt, cancellationToken);
}
