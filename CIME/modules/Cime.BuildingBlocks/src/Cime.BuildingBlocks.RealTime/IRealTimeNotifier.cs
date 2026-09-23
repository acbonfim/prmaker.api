namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Abstração injetável na camada de aplicação para publicar eventos de tempo real.
/// Adicionar um novo ponto de atualização em tempo real = injetar isto e chamar
/// NotifyGroupAsync/NotifyAllAsync após o commit da operação.
/// </summary>
public interface IRealTimeNotifier
{
    /// <summary>Envia um evento para todos os clientes inscritos em um grupo.</summary>
    Task NotifyGroupAsync(string group, string eventName, object? payload = null, CancellationToken cancellationToken = default);

    /// <summary>Envia um evento para todos os clientes conectados.</summary>
    Task NotifyAllAsync(string eventName, object? payload = null, CancellationToken cancellationToken = default);
}
