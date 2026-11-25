namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Saúde da entrega dos eventos nesta instância da API: quando a última publicação no relay falhou. Quem depende do
/// aviso em tempo real (o executor, que só consulta a fila quando avisado) usa isso para voltar a consultar mais cedo
/// enquanto o relay está instável. Por instância — com mais de uma, cada uma enxerga as próprias falhas.
/// </summary>
public class RealTimeDeliveryHealth(TimeProvider time)
{
    private long _lastFailureTicks;

    public DateTimeOffset? LastFailureAt =>
        Interlocked.Read(ref _lastFailureTicks) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    public void ReportFailure() => Interlocked.Exchange(ref _lastFailureTicks, time.GetUtcNow().UtcTicks);

    /// <summary>Alguma publicação falhou dentro da janela.</summary>
    public bool FailedWithin(TimeSpan window) => LastFailureAt is { } at && time.GetUtcNow() - at <= window;
}
