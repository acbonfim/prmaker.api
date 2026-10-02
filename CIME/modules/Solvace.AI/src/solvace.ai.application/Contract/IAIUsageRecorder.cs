using solvace.ai.domain.Responses;

namespace solvace.ai.application.Contract;

/// <summary>
/// Registra o consumo de cada chamada de IA (0042): quem, qual ação, modelo, tokens e custo estimado. Implementado pelo
/// host; falha no registro nunca derruba a chamada.
/// </summary>
public interface IAIUsageRecorder
{
    Task RecordAsync(AIGenerateResponse response, TimeSpan elapsed, CancellationToken cancellationToken);
}
