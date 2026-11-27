using System.Security.Claims;
using solvace.azure.domain.Models;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;

namespace solvace.prform.Execution;

/// <summary>0099 (Q1): cards do épico para o escopo <c>epic</c> do board — os <c>EpicCard</c> do épico.</summary>
public sealed class EpicCardSource(IEpicApplication epics) : IEpicCardSource
{
    public async Task<IReadOnlyList<string>?> GetCardsAsync(long epic, CancellationToken ct)
    {
        if (await epics.FindAsync(epic, ct) is null) return null;
        var detail = await epics.GetAsync(epic, ct);
        return detail.Cards.Select(c => c.Card).Where(c => !EpicKey.IsEpic(c)).ToList();
    }

    public async Task<EpicBoardInfo?> GetBoardInfoAsync(long epic, CancellationToken ct)
    {
        if (await epics.FindAsync(epic, ct) is not { } summary) return null;
        return new EpicBoardInfo(summary.DevOpsTeam, summary.DevOpsBoard, ToDevOps(summary.BoardFilters));
    }

    /// <summary>Filtros do épico (módulo dos planos) → filtros do board (módulo do DevOps), já limpos.</summary>
    public static DevOpsBoardFilters ToDevOps(EpicBoardFilters? f) =>
        solvace.azure.application.Services.DevOpsDeskMapping.NormalizeBoardFilters(f is null ? null : new DevOpsBoardFilters
        {
            Lanes = [.. f.Lanes], HiddenColumns = [.. f.HiddenColumns], Types = [.. f.Types], States = [.. f.States], AssignedTo = [.. f.AssignedTo], Tags = [.. f.Tags],
            Sla = [.. f.Sla], Text = f.Text ?? string.Empty
        });
}

/// <summary>0099 (Q1): card criado pela Mesa dentro de um épico entra nele como <c>created</c> (foto do título e do tipo).</summary>
public sealed class EpicCardCreatedHook(
    IEpicApplication epics, solvace.timeline.application.Contracts.IUserRepository users) : IDevOpsCardCreatedHook
{
    public async Task CardCreatedAsync(long epic, DevOpsCardDetail card, ClaimsPrincipal user, CancellationToken ct)
    {
        Guid? userId = Guid.TryParse(user.FindFirst("ExternalId")?.Value, out var id) ? id : null;
        string? name = userId.HasValue ? await users.GetFullNameAsync(userId.Value, ct) : null;
        name ??= user.FindFirst(ClaimTypes.Name)?.Value;
        var actor = new ExecutionActor(userId, string.IsNullOrWhiteSpace(name) ? "Claude" : name.Trim(), true);
        await epics.AddCardAsync(epic, new AddEpicCardRequest
        {
            Card = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Role = EpicCardRole.Created,
            Title = card.Title,
            WorkItemType = card.Type
        }, actor, ct);
    }
}
