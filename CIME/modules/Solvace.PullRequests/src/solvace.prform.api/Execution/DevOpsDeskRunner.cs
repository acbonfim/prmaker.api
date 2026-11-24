using System.Security.Claims;
using Cime.BuildingBlocks.RealTime;
using solvace.azure.application.Contract;
using solvace.azure.domain.Models;
using solvace.prform.Controllers;
using solvace.timeline.application.Contracts;

namespace solvace.prform.Execution;

/// <summary>0099: tempo real dos cards do DevOps gravados pelo PRMake (Mesa de gestão, especialista).</summary>
public static class DevOpsRealTimeEvents
{
    /// <summary>Grupo de quem está na Mesa (board aberto).</summary>
    public const string Group = "devops";

    /// <summary>Sinal <c>{ids: string[], action}</c> — a tela refaz o GET do board/card.</summary>
    public const string EventCardsUpdated = "devopsCardsUpdated";

    public static class Actions
    {
        public const string Created = "created";
        public const string Updated = "updated";
        public const string Moved = "moved";
        public const string Reordered = "reordered";
    }
}

/// <summary>
/// 0099: cards do épico (escopo <c>epic</c> do board). Implementação: <see cref="EpicCardSource"/>.
/// </summary>
public interface IEpicCardSource
{
    /// <summary>Números dos cards do DevOps do épico (null = épico não existe).</summary>
    Task<IReadOnlyList<string>?> GetCardsAsync(long epic, CancellationToken ct);
}

/// <summary>
/// 0099: card criado pela Mesa dentro de um épico — <see cref="EpicCardCreatedHook"/> grava o <c>EpicCard</c> (role <c>created</c>, foto do
/// título e do tipo) e o <c>epicsUpdated</c>. Falha aqui não desfaz o card (só é logada).
/// </summary>
public interface IDevOpsCardCreatedHook
{
    Task CardCreatedAsync(long epic, DevOpsCardDetail card, ClaimsPrincipal user, CancellationToken ct);
}

/// <summary>
/// 0099: gravações da Mesa no DevOps com o mesmo efeito pela tela (REST) e pelo especialista (MCP) — grava pelo
/// <see cref="IDevOpsDeskService"/>, registra na Timeline do card ("… pela Mesa de gestão", com a marca do especialista),
/// avisa o grupo <c>devops</c> (<c>devopsCardsUpdated</c>) e, card criado num épico, chama o <see cref="IDevOpsCardCreatedHook"/>.
/// </summary>
public class DevOpsDeskRunner(IDevOpsDeskService devops, ITimelineApplication timeline, IRealTimeNotifier realTime,
    IDevOpsCardCreatedHook createdHook, ILogger<DevOpsDeskRunner> logger)
{
    public async Task<DevOpsCardWriteResult> CreateAsync(DevOpsCreateCardRequest request, ClaimsPrincipal user, CancellationToken ct, string? via = null)
    {
        var result = await devops.CreateCardAsync(request, ct);
        if (request.Epic is > 0)
        {
            try
            {
                await createdHook.CardCreatedAsync(request.Epic.Value, result.Card, user, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Card {Card} criado no DevOps, mas não entrou no épico {Epic}", result.Card.Id, request.Epic);
            }
        }
        var epic = request.Epic is > 0 ? $" (épico #{request.Epic})" : string.Empty;
        await AfterAsync(result, result.Summary + epic, DevOpsRealTimeEvents.Actions.Created, user, via, ct, request.ParentId);
        return result;
    }

    public async Task<DevOpsCardWriteResult> UpdateAsync(int id, DevOpsUpdateCardRequest request, ClaimsPrincipal user, CancellationToken ct, string? via = null)
    {
        var result = await devops.UpdateCardAsync(id, request, ct);
        await AfterAsync(result, result.Summary, DevOpsRealTimeEvents.Actions.Updated, user, via, ct);
        return result;
    }

    public async Task<DevOpsCardWriteResult> MoveAsync(int id, DevOpsMoveCardRequest request, ClaimsPrincipal user, CancellationToken ct, string? via = null)
    {
        var result = await devops.MoveCardAsync(id, request, ct);
        var action = result.Action == DevOpsRealTimeEvents.Actions.Reordered ? DevOpsRealTimeEvents.Actions.Reordered : DevOpsRealTimeEvents.Actions.Moved;
        // Só a ordem mudou: não é marco do card (não vai para a Timeline), mas os outros boards abertos se atualizam.
        if (action == DevOpsRealTimeEvents.Actions.Reordered)
            await NotifyAsync([id.ToString()], action, ct);
        else
            await AfterAsync(result, result.Summary, action, user, via, ct);
        return result;
    }

    public async Task<int> CommentAsync(int id, string markdown, ClaimsPrincipal user, CancellationToken ct, string? via = null)
    {
        var commentId = await devops.CommentAsync(id, markdown, ct);
        await TimelineAsync(id.ToString(), $"Comentário publicado na discussion do card #{id}", user, via, ct);
        await NotifyAsync([id.ToString()], DevOpsRealTimeEvents.Actions.Updated, ct);
        return commentId;
    }

    private async Task AfterAsync(DevOpsCardWriteResult result, string summary, string action, ClaimsPrincipal user, string? via, CancellationToken ct,
        int? alsoNotify = null)
    {
        var card = result.Card.Id.ToString();
        await TimelineAsync(card, summary, user, via, ct);
        var ids = alsoNotify is > 0 ? new[] { card, alsoNotify.Value.ToString() } : [card];
        await NotifyAsync(ids, action, ct);
    }

    private Task TimelineAsync(string card, string summary, ClaimsPrincipal user, string? via, CancellationToken ct)
    {
        var text = string.IsNullOrWhiteSpace(via)
            ? $"{summary} pela Mesa de gestão do CIME."
            : $"{summary} pela Mesa de gestão do CIME — {via}.";
        return AzureController.RegisterOnTimelineAsync(timeline, card, text, logger, user, ct);
    }

    /// <summary>Tempo real é sinal: falha só é logada.</summary>
    private async Task NotifyAsync(string[] ids, string action, CancellationToken ct)
    {
        try
        {
            await realTime.NotifyGroupAsync(DevOpsRealTimeEvents.Group, DevOpsRealTimeEvents.EventCardsUpdated, new { ids, action }, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "devopsCardsUpdated não publicado ({Action} {Ids})", action, string.Join(",", ids));
        }
    }
}
