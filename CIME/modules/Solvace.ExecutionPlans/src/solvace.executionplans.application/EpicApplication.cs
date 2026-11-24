using System.Text;
using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0099: épicos da Mesa de gestão. Os itens, a conversa do especialista, os arquivos e os relatados do épico são os mesmos da
/// Bancada, pela chave <see cref="EpicKey"/> (<c>E{número}</c>) no lugar do número do card. Tempo real: <c>epicsUpdated</c>
/// no grupo <c>epics</c> (sinal; a tela refaz o GET).
/// </summary>
public class EpicApplication(IEpicRepository epics, IRealTimeNotifier realTime) : IEpicApplication
{
    private const int ListLimit = 300;
    private const int Retries = 4;
    private const int ContextCards = 80;

    // ================================================================ leitura

    public async Task<IReadOnlyList<EpicSummary>> ListAsync(string? status, string? query, Guid? createdBy, CancellationToken ct)
    {
        var list = await epics.ListAsync(ParseStatuses(status), Clean(query), createdBy, ListLimit, ct);
        return await SummariesAsync(list, ct);
    }

    public async Task<EpicResponse> GetAsync(long number, CancellationToken ct)
    {
        var epic = await epics.GetByNumberAsync(number, ct) ?? throw NotFound(number);
        return await ResponseAsync(epic, ct);
    }

    public async Task<EpicSummary?> FindAsync(long number, CancellationToken ct)
    {
        if (number <= 0) return null;
        var list = await epics.ListByNumbersAsync([number], ct);
        return list.Count == 0 ? null : (await SummariesAsync(list, ct))[0];
    }

    public async Task<IReadOnlyList<EpicSummary>> ListByCardAsync(string card, CancellationToken ct)
    {
        string normalized;
        try { normalized = EpicCard.NormalizeCard(card); }
        catch (DomainException) { return []; } // chave de épico ou lixo: nenhum épico
        return await SummariesAsync(await epics.ListByCardAsync(normalized, ct), ct);
    }

    public async Task<string> ContextMarkdownAsync(long number, CancellationToken ct)
    {
        var epic = await epics.GetByNumberAsync(number, ct) ?? throw NotFound(number);
        var counts = (await epics.CountsAsync([epic], ct)).GetValueOrDefault(epic.Id) ?? new EpicCounts();
        var sb = new StringBuilder();
        sb.AppendLine($"# Épico #{epic.Number} ({epic.Key}) — {epic.Title}");
        sb.AppendLine();
        sb.AppendLine($"- Status: **{epic.Status}** · criado por {epic.CreatedByName} em {epic.CreatedAt:dd/MM/yyyy} · atualizado {epic.UpdatedAt:dd/MM HH:mm} UTC");
        if (epic.Tags.Count > 0) sb.AppendLine($"- Tags: {string.Join(", ", epic.Tags)}");
        sb.AppendLine($"- Board preferido do DevOps: time {(epic.DevOpsTeam ?? "(não definido — pergunte ou use o padrão)")}, " +
                      $"board {(epic.DevOpsBoard ?? "(não definido)")}. Colunas, lanes e mapeamento coluna→estado: prmake_devops_meta(what: board).");
        if (epic.DevOpsCardId is { Length: > 0 } rep) sb.AppendLine($"- Card do DevOps que representa o épico: {rep}");
        sb.AppendLine($"- Itens de trabalho: {counts.Items} ({counts.OpenItems} abertos) · cards: {counts.Cards}");
        if (!string.IsNullOrWhiteSpace(epic.Description))
        {
            sb.AppendLine().AppendLine("## Descrição");
            sb.AppendLine(Cut(epic.Description, 4000));
        }

        sb.AppendLine().AppendLine($"## Cards do épico ({epic.Cards.Count})");
        if (epic.Cards.Count == 0)
            sb.AppendLine("Nenhum card ainda. Cards entram criados pela Mesa (prmake_devops_card_create com epic), à mão (prmake_epic_cards) " +
                          $"ou quando alguém cita #{epic.Number} na conversa/Timeline de um card.");
        foreach (var c in epic.Cards.OrderByDescending(c => c.AddedAt).Take(ContextCards))
        {
            var photo = string.Join(" · ", new[] { c.WorkItemType, c.Title }.Where(s => !string.IsNullOrWhiteSpace(s)));
            sb.AppendLine($"- {c.CardNumber}{(photo.Length > 0 ? " — " + photo : string.Empty)} ({RoleText(c.Role)} por {c.AddedByName}, {c.AddedAt:dd/MM})");
        }
        if (epic.Cards.Count > ContextCards)
            sb.AppendLine($"- … mais {epic.Cards.Count - ContextCards} card(s) — lista completa em prmake_epic({epic.Number}).");
        sb.AppendLine("Estado/coluna atuais dos cards: prmake_devops_query (filtro por ids) ou prmake_devops_card_get(id) — a lista acima é só a foto de quando entraram.");
        return sb.ToString();
    }

    // ================================================================ gravação

    public async Task<EpicResponse> CreateAsync(SaveEpicRequest req, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var epic = new Epic(req.Title, req.Description, req.Color, req.Tags, req.DevOpsTeam, req.DevOpsBoard, req.DevOpsCardId,
            actor.UserId, actor.Name, DateTimeOffset.UtcNow);
        epics.Add(epic);
        await epics.SaveChangesAsync(ct);
        await NotifyAsync(epic.Number, ExecutionPlanRealTimeEvents.EpicActions.Created, ct);
        return await ResponseAsync(epic, ct);
    }

    public async Task<EpicResponse> UpdateAsync(long number, SaveEpicRequest req, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var epic = await MutateAsync(number, e => e.Update(req.Title, req.Description, req.Color, req.Tags, req.DevOpsTeam, req.DevOpsBoard,
            req.DevOpsCardId, DateTimeOffset.UtcNow), ct);
        await NotifyAsync(number, ExecutionPlanRealTimeEvents.EpicActions.Updated, ct);
        return await ResponseAsync(epic, ct);
    }

    public async Task<EpicResponse> SetStatusAsync(long number, string? status, ExecutionActor actor, CancellationToken ct)
    {
        var changed = false;
        var epic = await MutateAsync(number, e => changed = e.SetStatus(status, DateTimeOffset.UtcNow), ct);
        if (changed)
            await NotifyAsync(number, ExecutionPlanRealTimeEvents.EpicActions.Status, ct);
        return await ResponseAsync(epic, ct);
    }

    public async Task<EpicCardResponse> AddCardAsync(long number, AddEpicCardRequest req, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        EpicCard? card = null;
        var changed = false;
        await MutateAsync(number, e =>
        {
            (card, changed) = e.AddCard(req.Card, req.Role, req.Title, req.WorkItemType, actor.UserId, actor.Name, DateTimeOffset.UtcNow);
        }, ct);
        if (changed)
            await NotifyAsync(number, ExecutionPlanRealTimeEvents.EpicActions.Cards, ct);
        return EpicCardResponse.From(card!);
    }

    public async Task RemoveCardAsync(long number, string card, ExecutionActor actor, CancellationToken ct)
    {
        EpicCard? removed = null;
        await MutateAsync(number, e => removed = e.RemoveCard(card, DateTimeOffset.UtcNow), ct);
        if (removed is not null)
            await NotifyAsync(number, ExecutionPlanRealTimeEvents.EpicActions.Cards, ct);
    }

    public async Task<int> CaptureMentionsAsync(string card, IReadOnlyCollection<long> numbers, ExecutionActor actor, CancellationToken ct)
    {
        if (numbers.Count == 0 || EpicKey.IsEpic(card)) return 0;
        string normalized;
        try { normalized = EpicCard.NormalizeCard(card); }
        catch (DomainException) { return 0; }

        for (var attempt = 1; ; attempt++)
        {
            var found = await epics.GetByNumbersAsync(numbers.Where(n => n > 0).Distinct().ToList(), ct);
            if (found.Count == 0) return 0;
            var changed = new List<long>();
            var now = DateTimeOffset.UtcNow;
            foreach (var epic in found)
                if (epic.AddCard(normalized, EpicCardRole.Mentioned, null, null, actor.UserId, actor.Name, now).Changed)
                    changed.Add(epic.Number);
            if (changed.Count == 0) return 0;
            try
            {
                await epics.SaveChangesAsync(ct);
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < Retries)
            {
                epics.Detach();
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, ct);
                continue;
            }
            foreach (var n in changed)
                await NotifyAsync(n, ExecutionPlanRealTimeEvents.EpicActions.Cards, ct);
            return changed.Count;
        }
    }

    // ================================================================ internos

    /// <summary>Carrega, muda e salva com novas tentativas em conflito (xmin ou card repetido ao mesmo tempo).</summary>
    private async Task<Epic> MutateAsync(long number, Action<Epic> change, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var epic = await epics.GetByNumberAsync(number, ct) ?? throw NotFound(number);
            change(epic);
            try
            {
                await epics.SaveChangesAsync(ct);
                return epic;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < Retries)
            {
                epics.Detach();
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, ct);
            }
        }
    }

    private async Task<EpicResponse> ResponseAsync(Epic epic, CancellationToken ct)
    {
        var counts = (await epics.CountsAsync([epic], ct)).GetValueOrDefault(epic.Id) ?? new EpicCounts { Cards = epic.Cards.Count };
        counts.Cards = epic.Cards.Count;
        return EpicResponse.From(epic, counts);
    }

    private async Task<List<EpicSummary>> SummariesAsync(IReadOnlyList<Epic> list, CancellationToken ct)
    {
        if (list.Count == 0) return [];
        var counts = await epics.CountsAsync(list, ct);
        return list.Select(e => EpicSummary.From(e, counts.GetValueOrDefault(e.Id) ?? new EpicCounts())).ToList();
    }

    /// <summary>Vazio → todos menos arquivados; <c>all</c> → todos; senão a lista (validada).</summary>
    public static IReadOnlyCollection<string>? ParseStatuses(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Length == 0) return [EpicStatus.Active, EpicStatus.Paused, EpicStatus.Done];
        if (s is "all" or "todos") return null;
        return s.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(EpicStatus.Normalize).Distinct().ToList();
    }

    private Task NotifyAsync(long number, string action, CancellationToken ct) =>
        TryAsync(() => realTime.NotifyGroupAsync(ExecutionPlanRealTimeEvents.EpicsGroup, ExecutionPlanRealTimeEvents.EventEpicsUpdated,
            new { number, key = EpicKey.Of(number), action }, ct));

    private static async Task TryAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OperationCanceledException) { }
    }

    private static string RoleText(string role) => role switch
    {
        EpicCardRole.Created => "criado pela Mesa",
        EpicCardRole.Mentioned => "mencionado",
        _ => "adicionado"
    };

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static ExecutionPlanNotFoundException NotFound(long number) => new($"Épico #{number} não encontrado.");
}
