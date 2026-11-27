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
/// no grupo <c>epics</c> (sinal; a tela refaz o GET). Ao criar, o especialista já abre a conversa do épico com a apresentação
/// (<see cref="WelcomeText"/>).
/// </summary>
public class EpicApplication(IEpicRepository epics, IRealTimeNotifier realTime, IEpicStoryRepository? stories = null,
    ICaseChatRepository? cases = null) : IEpicApplication
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
                      $"board {(epic.DevOpsBoard ?? "(não definido)")}. Cards do board (com os filtros salvos do épico): prmake_devops_board(epic: {epic.Number}); " +
                      "colunas, lanes e mapeamento coluna→estado: prmake_devops_meta(what: board).");
        if (epic.DevOpsCardId is { Length: > 0 } rep) sb.AppendLine($"- Card do DevOps que representa o épico: {rep}");
        AppendBoardFilters(sb, epic);
        sb.AppendLine(epic.AllowCardStoryDrafts
            ? "- Rascunhos de histórias pelos cards: **permitidos** — o Especialista do caso de um card do épico (criado ou vinculado) escreve rascunhos aqui; converter em card é só pela Mesa (admin/management)."
            : "- Rascunhos de histórias pelos cards: **desligados** — histórias só pela Mesa de gestão.");
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
        // Ajustes 2: histórias/bugs escritos no PRMake (rascunhos × convertidas em card).
        if (stories is not null)
            sb.AppendLine().Append(EpicStoryApplication.ContextMarkdown(epic.Number, await stories.ListByEpicAsync(epic.Id, null, ct), DateTimeOffset.UtcNow));
        return sb.ToString();
    }

    // ================================================================ gravação

    public async Task<EpicResponse> CreateAsync(SaveEpicRequest req, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var epic = new Epic(req.Title, req.Description, req.Color, req.Tags, req.DevOpsTeam, req.DevOpsBoard, req.DevOpsCardId,
            actor.UserId, actor.Name, DateTimeOffset.UtcNow);
        if (req.BoardFilters is not null) epic.SetBoardFilters(req.BoardFilters);
        epic.SetAllowCardStoryDrafts(req.AllowCardStoryDrafts ?? true, epic.CreatedAt);
        epics.Add(epic);
        await epics.SaveChangesAsync(ct);
        await NotifyAsync(epic.Number, ExecutionPlanRealTimeEvents.EpicActions.Created, ct);
        var response = await ResponseAsync(epic, ct);
        await WelcomeAsync(epic, response, ct);
        return response;
    }

    public async Task<EpicResponse> UpdateAsync(long number, SaveEpicRequest req, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var epic = await MutateAsync(number, e =>
        {
            e.Update(req.Title, req.Description, req.Color, req.Tags, req.DevOpsTeam, req.DevOpsBoard, req.DevOpsCardId, DateTimeOffset.UtcNow);
            // Ajustes 1: filtros do board só quando vierem (null = não mexe).
            if (req.BoardFilters is not null) e.SetBoardFilters(req.BoardFilters);
            // 0104: rascunhos pelos cards — só quando vier (null = não mexe).
            if (req.AllowCardStoryDrafts is { } allow) e.SetAllowCardStoryDrafts(allow, DateTimeOffset.UtcNow);
        }, ct);
        await NotifyAsync(number, ExecutionPlanRealTimeEvents.EpicActions.Updated, ct);
        return await ResponseAsync(epic, ct);
    }

    public async Task<EpicResponse> SetBoardFiltersAsync(long number, EpicBoardFilters? filters, ExecutionActor actor, CancellationToken ct)
    {
        var changed = false;
        var epic = await MutateAsync(number, e => changed = e.SetBoardFilters(filters ?? new EpicBoardFilters()), ct);
        if (changed)
            await NotifyAsync(number, ExecutionPlanRealTimeEvents.EpicActions.Filters, ct);
        return await ResponseAsync(epic, ct);
    }

    public async Task<IReadOnlyList<string>> TagsAsync(string? query, CancellationToken ct) =>
        CountTags(await epics.ListTagsJsonAsync(ct), query);

    /// <summary>
    /// Tags dos épicos (todas as situações) sem repetir (sem caixa; fica a grafia mais usada), mais usadas primeiro e depois pelo
    /// nome; <paramref name="query"/> filtra por "contém" sem caixa/acento.
    /// </summary>
    public static IReadOnlyList<string> CountTags(IEnumerable<string?> tagsJson, string? query)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in tagsJson)
        {
            List<string>? tags;
            try { tags = string.IsNullOrWhiteSpace(json) ? null : System.Text.Json.JsonSerializer.Deserialize<List<string>>(json); }
            catch (System.Text.Json.JsonException) { tags = null; }
            foreach (var raw in (tags ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var tag = raw?.Trim();
                if (string.IsNullOrEmpty(tag)) continue;
                if (!counts.TryGetValue(tag, out var spellings)) counts[tag] = spellings = new Dictionary<string, int>(StringComparer.Ordinal);
                spellings[tag] = spellings.GetValueOrDefault(tag) + 1;
            }
        }
        var term = Fold(query);
        return counts
            .Select(c => (Tag: c.Value.OrderByDescending(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key, Count: c.Value.Values.Sum()))
            .Where(t => term.Length == 0 || Fold(t.Tag).Contains(term, StringComparison.Ordinal))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.Tag)
            .ToList();
    }

    /// <summary>Sem acento e em minúsculas (busca das tags).</summary>
    private static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var ch in text.Trim().Normalize(NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
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

    // ================================================================ apresentação do especialista

    /// <summary>
    /// Primeira mensagem da conversa do épico: o especialista se apresenta com o estado do épico e o que sabe fazer (texto
    /// montado aqui, sem IA). Best-effort — falha não desfaz o épico; conversa que já tem mensagem não ganha outra.
    /// </summary>
    private async Task WelcomeAsync(Epic epic, EpicResponse response, CancellationToken ct)
    {
        if (cases is null) return;
        var key = EpicKey.Of(epic.Number);
        var text = WelcomeText(epic, response.Counts, stories: 0); // épico recém-criado: nenhuma história ainda
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                cases.ClearTracking();
                if (await cases.GetMaxMessageNumberAsync(key, ct) > 0) return;
                var now = DateTimeOffset.UtcNow;
                cases.AddMessage(new CaseMessage(key, 1, CaseAuthorKind.Specialist, null, CaseChatApplication.SpecialistName,
                    CaseMessageKind.Text, text, false, now));
                if (await cases.GetChatAsync(key, ct) is null)
                    cases.AddChat(new CaseChat(key, now));
                await cases.SaveChangesAsync(ct);
                await TryAsync(() => realTime.NotifyGroupAsync(ExecutionPlanRealTimeEvents.Group(key), ExecutionPlanRealTimeEvents.EventCaseUpdated,
                    new { cardNumber = key, action = ExecutionPlanRealTimeEvents.CaseActions.Message, number = 1 }, ct));
                return;
            }
            catch (CaseNumberConflictException) when (attempt < Retries)
            {
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Apresentação do especialista do épico: quem é, como está o épico, o board e o que pode fazer — curta e direta.</summary>
    public static string WelcomeText(Epic epic, EpicCounts? counts, int stories)
    {
        var cards = counts?.Cards ?? epic.Cards.Count;
        var items = counts?.Items ?? 0;
        var sb = new StringBuilder();
        sb.AppendLine($"Olá! Sou o especialista do épico #{epic.Number} — {epic.Title}.");
        sb.AppendLine();

        var numbers = $"{Plural(cards, "card", "cards")}, {Plural(items, "item de trabalho", "itens de trabalho")} e {Plural(stories, "história", "histórias")}";
        var status = StatusText(epic.Status);
        var empty = cards == 0 && items == 0 && stories == 0;
        var state = empty ? $"O épico está {status}, mas ainda vazio: {numbers}." : $"O épico está {status}, com {numbers}.";
        sb.AppendLine($"{state} {BoardText(epic)}");
        sb.AppendLine();
        sb.AppendLine("Posso, por exemplo:");
        sb.AppendLine();
        sb.AppendLine("* listar ou consultar cards no DevOps, ou ver o board do time;");
        sb.AppendLine("* criar, editar ou mover cards, e adicioná-los ao épico;");
        sb.AppendLine("* escrever histórias e bugs no épico e convertê-los em card;");
        sb.AppendLine("* fazer a triagem em lote dos cards novos, com sugestão de área, prioridade e tags;");
        sb.AppendLine("* apontar cards parados por coluna e a saúde do board;");
        sb.AppendLine("* gerar relatório de bugs, procurar duplicados ou fazer RCA em PDF;");
        sb.AppendLine(cards == 0
            ? "* acompanhar as entregas (PRs e ambientes) quando houver cards no épico."
            : "* acompanhar as entregas (PRs e ambientes) dos cards do épico.");
        sb.AppendLine();
        sb.Append(HasBoard(epic)
            ? "O que você quer ver primeiro? Se for o board do time, posso começar por ele."
            : "O que você quer ver primeiro? Se quiser, começo definindo o board do time do épico.");
        return sb.ToString();
    }

    private static bool HasBoard(Epic epic) => !string.IsNullOrWhiteSpace(epic.DevOpsTeam);

    private static string BoardText(Epic epic)
    {
        if (!HasBoard(epic))
            return "Ainda não há board preferido — me diga o time do DevOps (ou defina no épico).";
        var board = string.IsNullOrWhiteSpace(epic.DevOpsBoard) ? epic.DevOpsTeam!.Trim() : $"{epic.DevOpsTeam!.Trim()} / {epic.DevOpsBoard.Trim()}";
        var f = epic.BoardFilters;
        if (!f.HasAny()) return $"O board preferido é {board}.";
        var parts = new List<string>();
        if (f.Lanes.Count > 0) parts.Add($"nas lanes {JoinAnd(f.Lanes.Select(l => l.Length == 0 ? "(padrão)" : l))}");
        if (f.Types.Count > 0) parts.Add($"nos tipos {JoinAnd(f.Types)}");
        if (f.States.Count > 0) parts.Add($"nos estados {JoinAnd(f.States)}");
        if (f.AssignedTo.Count > 0) parts.Add($"do responsável {JoinAnd(f.AssignedTo.Select(a => a == "__unassigned__" ? "(sem responsável)" : a))}");
        if (f.Tags.Count > 0) parts.Add($"com as tags {JoinAnd(f.Tags)}");
        if (!string.IsNullOrWhiteSpace(f.Text)) parts.Add($"com o texto \"{f.Text.Trim()}\"");
        if (f.HiddenColumns.Count > 0) parts.Add($"sem as colunas {JoinAnd(f.HiddenColumns)}");
        return $"O board preferido é {board}, com filtro salvo {JoinAnd(parts)}.";
    }

    private static string StatusText(string status) => status switch
    {
        EpicStatus.Paused => "pausado",
        EpicStatus.Done => "concluído",
        EpicStatus.Archived => "arquivado",
        _ => "ativo"
    };

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private static string JoinAnd(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count <= 1 ? string.Join(string.Empty, list) : $"{string.Join(", ", list.Take(list.Count - 1))} e {list[^1]}";
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

    /// <summary>
    /// Ajustes 1: filtros do board salvos no épico (a tela grava; o especialista considera no board do time) e a regra de uso.
    /// </summary>
    private static void AppendBoardFilters(StringBuilder sb, Epic epic)
    {
        var f = epic.BoardFilters;
        if (!f.HasAny())
        {
            sb.AppendLine("- Filtros salvos do board: nenhum (o board do time aparece inteiro, todas as colunas e lanes).");
            return;
        }
        var parts = new List<string>();
        if (f.HiddenColumns.Count > 0) parts.Add("colunas ocultas " + string.Join(", ", f.HiddenColumns));
        if (f.Lanes.Count > 0) parts.Add("lanes escolhidas " + string.Join(", ", f.Lanes.Select(l => l.Length == 0 ? "(padrão)" : l)));
        if (f.Types.Count > 0) parts.Add("tipos " + string.Join(", ", f.Types));
        if (f.States.Count > 0) parts.Add("estados " + string.Join(", ", f.States));
        if (f.AssignedTo.Count > 0) parts.Add("responsável " + string.Join(", ", f.AssignedTo.Select(a => a == "__unassigned__" ? "(sem responsável)" : a)));
        if (f.Tags.Count > 0) parts.Add("tags (todas) " + string.Join(", ", f.Tags));
        if (!string.IsNullOrWhiteSpace(f.Text)) parts.Add($"texto \"{f.Text}\"");
        sb.AppendLine($"- Filtros salvos do board: {string.Join(" · ", parts)}.");
        sb.AppendLine("  No board do time, considere os filtros salvos do épico — `prmake_devops_board(epic)` já aplica; para ver além deles, " +
                      "passe useEpicFilters=false e diga isso na resposta.");
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
