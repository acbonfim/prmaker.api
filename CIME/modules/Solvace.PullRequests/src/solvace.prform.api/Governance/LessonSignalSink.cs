using solvace.executionplans.application.Contracts;
using solvace.governance.application;

namespace solvace.prform.Governance;

/// <summary>0106: sinais de erro do fluxo dos planos/itens viram sinais das lições (governança). Best-effort.</summary>
public class LessonSignalSink(IGovernanceLessonsApplication lessons, ILogger<LessonSignalSink> logger) : ILessonSignalSink
{
    public async Task SignalAsync(string card, string kind, string key, string text, string? reference, CancellationToken ct)
    {
        try
        {
            await lessons.SignalAsync(card, kind, key, text, reference, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Sinal de erro não registrado ({Kind} {Key}) no card {Card}", kind, key, card);
        }
    }
}
