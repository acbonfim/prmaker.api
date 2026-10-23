using solvace.ai.domain.Responses;

namespace solvace.ai.application.Contract;

public interface IAIService
{
    Task<AIGenerateResponse?> GenerateContentAsync(string prompt, CancellationToken cancellationToken = default);
}

