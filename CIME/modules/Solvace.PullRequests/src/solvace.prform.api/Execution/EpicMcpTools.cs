using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// 0099: épicos da Mesa de gestão e PDF pelo MCP — listar/detalhar/criar/editar épicos, cards do épico e markdown → PDF nos
/// arquivos de um item. Com o token do caso: num épico (chave <c>E…</c>), mexe no próprio épico e cria outros; num card, lê
/// épicos, cria épico e põe/tira só o próprio card de um épico. <c>prmake_epic_save</c>, <c>prmake_epic_cards</c> e
/// <c>prmake_pdf</c> são ações (rodam como quem pediu — <see cref="CaseSpecialistMiddleware.ActingTools"/>).
/// </summary>
[McpServerToolType]
public class EpicMcpTools(
    IEpicApplication epics,
    IWorkItemApplication items,
    WorkItemPdfService pdf,
    IHttpContextAccessor http,
    solvace.timeline.application.Contracts.IUserRepository users,
    SpecialistActions specialist)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [McpServerTool(Name = "prmake_epics", ReadOnly = true)]
    [Description("Epicos da Mesa de gestao (mais recentes primeiro): numero (#n, mesma numeracao dos itens), chave E<n>, titulo, status, " +
                 "cor, tags, time/board preferido do DevOps e contagens (cards, itens, abertos). q = parte do titulo, tag, numero ou E<n>; " +
                 "status = active|paused|done|archived (varios por virgula; vazio = todos menos arquivados; all = todos). Antes de criar um " +
                 "epico, procure um parecido aqui.")]
    public Task<string> List(string? q = null, string? status = null, CancellationToken ct = default) => Safe(async () =>
    {
        var list = await epics.ListAsync(status, q, null, ct);
        return Serialize(new
        {
            total = list.Count,
            epics = list.Take(100).Select(e => new
            {
                e.Number, e.Key, e.Title, e.Status, e.Color, e.Tags, e.DevOpsTeam, e.DevOpsBoard, e.DevOpsCardId,
                e.CreatedByName, e.UpdatedAt, e.Counts
            })
        });
    });

    [McpServerTool(Name = "prmake_epic", ReadOnly = true)]
    [Description("Detalhe do epico: dados, descricao, cards do DevOps (papel created|linked|mentioned, foto de titulo/tipo) e contagens. " +
                 "Os itens de trabalho do epico sao os do card E<n> (prmake_work_items(card: \"E<n>\")). Estado/coluna atuais dos cards: " +
                 "prmake_devops_query/prmake_devops_card_get.")]
    public Task<string> Get([Description("Numero do epico (#n) ou a chave E<n>")] string number, CancellationToken ct = default) => Safe(async () =>
        Serialize(await epics.GetAsync(ParseNumber(number), ct)));

    [McpServerTool(Name = "prmake_epic_save")]
    [Description("Cria (sem number) ou edita (com number) um epico da Mesa de gestao. Na edicao, campos nao enviados ficam como estao " +
                 "(tags/description vazios limpam so quando enviados). status = active|paused|done|archived (pausar, concluir, arquivar). " +
                 "Responda com #<numero> e o link auth/desk?epic=<numero>. Antes de criar, prmake_epics(q) para nao duplicar.")]
    public Task<string> Save(
        [Description("Titulo (obrigatorio ao criar)")] string? title = null,
        [Description("Numero do epico (#n ou E<n>) para editar; vazio cria um novo")] string? number = null,
        [Description("Descricao em markdown")] string? description = null,
        [Description("Cor hexa, ex.: #7a5cff")] string? color = null,
        [Description("Tags (lista ou texto separado por virgula)")] JsonElement? tags = null,
        [Description("Time do DevOps preferido do board do epico")] string? devOpsTeam = null,
        [Description("Board do DevOps preferido (ex.: Stories, Backlog items)")] string? devOpsBoard = null,
        [Description("Card do DevOps que representa o epico (Epic/Feature), opcional")] string? devOpsCardId = null,
        [Description("active | paused | done | archived")] string? status = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var actor = await ActorAsync(ct);
        var tagList = Tags(tags);
        if (string.IsNullOrWhiteSpace(number))
        {
            if (string.IsNullOrWhiteSpace(title)) throw new McpException("Informe o titulo do epico.");
            var created = await epics.CreateAsync(new SaveEpicRequest
            {
                Title = title, Description = description, Color = color, Tags = tagList, DevOpsTeam = devOpsTeam,
                DevOpsBoard = devOpsBoard, DevOpsCardId = devOpsCardId
            }, actor, ct);
            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status.Trim(), EpicStatus.Active, StringComparison.OrdinalIgnoreCase))
                created = await epics.SetStatusAsync(created.Number, status, actor, ct);
            return Serialize(new { created = true, created.Number, created.Key, link = $"auth/desk?epic={created.Number}", epic = created });
        }

        var n = ParseNumber(number);
        EnsureOwnEpic(n);
        var current = await epics.GetAsync(n, ct);
        var changesData = title is not null || description is not null || color is not null || tagList is not null || devOpsTeam is not null
                          || devOpsBoard is not null || devOpsCardId is not null;
        var saved = current;
        if (changesData)
            saved = await epics.UpdateAsync(n, new SaveEpicRequest
            {
                Title = title ?? current.Title,
                Description = description ?? current.Description,
                Color = color ?? current.Color,
                Tags = tagList ?? current.Tags,
                DevOpsTeam = devOpsTeam ?? current.DevOpsTeam,
                DevOpsBoard = devOpsBoard ?? current.DevOpsBoard,
                DevOpsCardId = devOpsCardId ?? current.DevOpsCardId
            }, actor, ct);
        if (!string.IsNullOrWhiteSpace(status))
            saved = await epics.SetStatusAsync(n, status, actor, ct);
        return Serialize(new { updated = true, saved.Number, saved.Key, link = $"auth/desk?epic={saved.Number}", epic = saved });
    });

    [McpServerTool(Name = "prmake_epic_cards")]
    [Description("Cards do DevOps do epico: add = cards a pôr (papel linked; nunca rebaixa created), remove = cards a tirar (so a pedido " +
                 "explicito — tirar do epico nao mexe no card). Devolve a lista atual. Card criado pela Mesa ja entra sozinho " +
                 "(prmake_devops_card_create com epic).")]
    public Task<string> Cards(
        [Description("Numero do epico (#n ou E<n>)")] string number,
        [Description("Cards a adicionar (lista ou texto separado por virgula)")] JsonElement? add = null,
        [Description("Cards a remover (lista ou texto separado por virgula)")] JsonElement? remove = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var n = ParseNumber(number);
        var toAdd = Tags(add) ?? [];
        var toRemove = Tags(remove) ?? [];
        EnsureCardsAllowed(n, toAdd.Concat(toRemove));
        var actor = await ActorAsync(ct);
        var errors = new List<string>();
        var added = new List<string>();
        var removed = new List<string>();
        foreach (var card in toAdd)
        {
            try { added.Add((await epics.AddCardAsync(n, new AddEpicCardRequest { Card = card, Role = EpicCardRole.Linked }, actor, ct)).Card); }
            catch (DomainException e) { errors.Add($"{card}: {e.Message}"); }
        }
        foreach (var card in toRemove)
        {
            try { await epics.RemoveCardAsync(n, card, actor, ct); removed.Add(card); }
            catch (DomainException e) { errors.Add($"{card}: {e.Message}"); }
        }
        var epic = await epics.GetAsync(n, ct);
        return Serialize(new
        {
            epic.Number, epic.Key, added, removed,
            errors = errors.Count > 0 ? errors : null,
            cards = epic.Cards
        });
    });

    [McpServerTool(Name = "prmake_pdf")]
    [Description("Converte um markdown em PDF NO SERVIDOR e grava nos arquivos do item de trabalho (kind pdf) — titulos, listas, tabelas, " +
                 "codigo, citacoes, links e imagens (data: ou arquivos do item citados como ![legenda](anexo:<nome>)). Cabecalho com titulo " +
                 "e data, rodape com a pagina. Mesmo nome substitui. Vale para item de epico (card E<n>) ou de card.")]
    public Task<string> Pdf(string card,
        [Description("Numero do item de trabalho (#n)")] long item,
        [Description("Nome do arquivo (ganha .pdf se faltar)")] string name,
        [Description("Markdown completo do documento")] string markdown,
        [Description("Titulo do PDF (padrao: o primeiro titulo do markdown)")] string? title = null,
        [Description("Numero da sub-tarefa do item (opcional)")] long? task = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = SkillsRulesService.NormalizeCard(card);
        if (c.Length == 0) throw new McpException("Informe o card (ou a chave E<n> do epico).");
        if (EpicKey.Normalize(c) is { } key) c = key;
        if (specialist.OwnCard is { } own && !string.Equals(EpicKey.Normalize(own) ?? own, c, StringComparison.Ordinal))
            throw new McpException("O Especialista do caso so grava PDF nos itens do card/epico dele.");
        var response = await items.GetAsync(item, withLogs: false, ct);
        if (!string.Equals(response.CardNumber, c, StringComparison.Ordinal))
            throw new McpException($"O item #{item} e do {response.CardNumber}, nao do {c}.");
        var saved = await pdf.SaveAsync(response, name, title, markdown, task, await ActorAsync(ct), ct);
        return Serialize(new { saved.Id, saved.Number, saved.Name, saved.Kind, saved.Size, saved.TaskNumber, saved.ContentUrl });
    });

    // ── Internos ─────────────────────────────────────────────────────────────────────────────────

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    /// <summary>Número do épico: <c>123</c>, <c>#123</c> ou <c>E123</c>.</summary>
    public static long ParseNumber(string? number)
    {
        var v = (number ?? string.Empty).Trim().TrimStart('#').Trim();
        if (EpicKey.TryParse(v, out var byKey)) return byKey;
        return long.TryParse(v, out var n) && n > 0 ? n : throw new McpException($"Numero de epico invalido: '{number}' (use 123 ou E123).");
    }

    /// <summary>Com o token do caso, editar só o épico do próprio caso.</summary>
    private void EnsureOwnEpic(long number)
    {
        if (specialist.OwnCard is not { } own) return;
        if (!EpicKey.TryParse(own, out var ownEpic) || ownEpic != number)
            throw new McpException("O Especialista do caso so edita o epico do caso dele (crie outro epico sem number, se for o pedido).");
    }

    /// <summary>Com o token do caso: no épico do caso, qualquer card; na conversa de um card, só o próprio card.</summary>
    private void EnsureCardsAllowed(long number, IEnumerable<string> cards)
    {
        if (specialist.OwnCard is not { } own) return;
        if (EpicKey.TryParse(own, out var ownEpic))
        {
            if (ownEpic != number)
                throw new McpException("O Especialista do caso so mexe nos cards do epico dele.");
            return;
        }
        if (cards.Any(c => !string.Equals(SkillsRulesService.NormalizeCard(c), own, StringComparison.Ordinal)))
            throw new McpException($"Na conversa do card {own}, o especialista so poe/tira o proprio card de um epico.");
    }

    private async Task<ExecutionActor> ActorAsync(CancellationToken ct)
    {
        if (specialist.Actor() is { } requested)
            return requested;
        Guid? userId = Guid.TryParse(User.FindFirst("ExternalId")?.Value, out var id) ? id : null;
        string? name = userId.HasValue ? await users.GetFullNameAsync(userId.Value, ct) : null;
        name ??= User.FindFirst(ClaimTypes.Name)?.Value;
        return new ExecutionActor(userId, string.IsNullOrWhiteSpace(name) ? "Claude" : name.Trim(), true);
    }

    /// <summary>Lista JSON ou texto separado por vírgula/ponto e vírgula (null = não enviado).</summary>
    private static List<string>? Tags(JsonElement? value)
    {
        if (value is not { } v || v.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.Array)
            return v.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
        var text = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
        if (string.IsNullOrWhiteSpace(text)) return [];
        text = text.Trim();
        if (text.StartsWith('['))
        {
            try { return Tags(JsonDocument.Parse(text).RootElement.Clone()); }
            catch (JsonException) { /* texto comum */ }
        }
        return text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    private static async Task<T> Safe<T>(Func<Task<T>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception e) when (e is DomainException or ExecutionPlanNotFoundException or ExecutionForbiddenException
                                       or ExecutionPlanConcurrencyException or ArgumentException or InvalidOperationException)
        {
            throw new McpException(e.Message, e);
        }
    }
}
