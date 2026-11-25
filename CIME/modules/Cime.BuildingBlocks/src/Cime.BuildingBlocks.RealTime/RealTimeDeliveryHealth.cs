namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Saúde da entrega dos eventos nesta instância da API: quando a última publicação no relay falhou. Quem depende do
/// aviso em tempo real (o executor, que só consulta a fila quando avisado) usa isso para voltar a consultar mais cedo
/// enquanto o relay está instável. Por instância — com mais de uma, cada uma enxerga as próprias falhas.
/// </summary>
public class RealTimeDeliveryHealth(TimeProvider time)
{
    /// <summary>Relay falhando: uma publicação a cada intervalo destes ainda espera o tempo inteiro (a "sonda").</summary>
    public static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(15);

    private long _lastFailureTicks;
    private long _lastSuccessTicks;
    private long _lastProbeTicks;

    public DateTimeOffset? LastFailureAt =>
        Interlocked.Read(ref _lastFailureTicks) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    public void ReportFailure() => Interlocked.Exchange(ref _lastFailureTicks, time.GetUtcNow().UtcTicks);

    /// <summary>A publicação passou (sai do modo degradado; a janela de <see cref="FailedWithin"/> continua contando).</summary>
    public void ReportSuccess() => Interlocked.Exchange(ref _lastSuccessTicks, time.GetUtcNow().UtcTicks);

    /// <summary>A última publicação falhou e faz menos de <paramref name="window"/>: o relay está fora agora.</summary>
    public bool IsDegraded(TimeSpan window) =>
        FailedWithin(window) && Interlocked.Read(ref _lastFailureTicks) > Interlocked.Read(ref _lastSuccessTicks);

    /// <summary>Alguma publicação falhou dentro da janela.</summary>
    public bool FailedWithin(TimeSpan window) => LastFailureAt is { } at && time.GetUtcNow() - at <= window;

    /// <summary>
    /// Relay falhando: esta publicação é a sonda (espera o tempo inteiro para ver se o relay voltou)? No máximo uma a cada
    /// <see cref="ProbeEvery"/>; as outras usam o tempo curto e não seguram a requisição.
    /// </summary>
    public bool TryTakeProbe()
    {
        var now = time.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastProbeTicks);
        return now - last >= ProbeEvery.Ticks && Interlocked.CompareExchange(ref _lastProbeTicks, now, last) == last;
    }
}
