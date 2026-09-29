using Cime.BuildingBlocks.RealTime;
using solvace.prform.domain.RealTime;

namespace solvace.prform.application;

public static class PullRequestRealTimeNotifications
{
    /// <summary>
    /// Avisa quem está com o card aberto na tela que algo mudou (ver PullRequestRealTimeEvents.Actions) e,
    /// quando a ação muda as listas de recentes, também quem está na home.
    /// Best-effort: falha na notificação não afeta a operação que já foi gravada.
    /// </summary>
    public static async Task NotifyCardUpdatedAsync(this IRealTimeNotifier notifier, string cardNumber, string action,
        int? id = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
            return;

        try
        {
            await notifier.NotifyGroupAsync(
                PullRequestRealTimeEvents.CardGroup(cardNumber),
                PullRequestRealTimeEvents.EventCardUpdated,
                new { cardNumber = cardNumber.Trim(), action, id },
                cancellationToken);

            if (PullRequestRealTimeEvents.Actions.AffectsRecent(action))
                await notifier.NotifyGroupAsync(
                    PullRequestRealTimeEvents.RecentGroup,
                    PullRequestRealTimeEvents.EventRecentUpdated,
                    new { cardNumber = cardNumber.Trim(), action },
                    cancellationToken);
        }
        catch
        {
            // Tempo real é best-effort.
        }
    }
}
