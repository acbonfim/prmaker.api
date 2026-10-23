using solvace.ai.domain.Requests;
using solvace.ai.domain.Responses;

namespace solvace.ai.application.Contract;

public interface IAIService
{
    Task<AIGenerateResponse?> GenerateContentAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 0072: com anexos (imagem, PDF, texto). Padrão: o provedor não os aceita — só o Claude os implementa por enquanto.
    /// </summary>
    Task<AIGenerateResponse?> GenerateContentAsync(string prompt, IReadOnlyList<AIAttachment> attachments, CancellationToken cancellationToken = default) =>
        attachments.Count == 0
            ? GenerateContentAsync(prompt, cancellationToken)
            : Task.FromResult<AIGenerateResponse?>(new AIGenerateResponse { Error = "O provedor de IA configurado não aceita anexos (use o Claude)." });
}
