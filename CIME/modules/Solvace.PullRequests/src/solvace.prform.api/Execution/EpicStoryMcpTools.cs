using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.azure.domain.Exceptions;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.prform.application.UserIntegrations;

namespace solvace.prform.Execution;

/// <summary>
/// 0099 (ajustes 2): histórias e bugs do épico pelo MCP — o especialista de gestão escreve rascunhos (critérios de aceite / passos de
/// reprodução claros) e só converte em card quando a pessoa pede. Com o token do caso: só o épico do caso (chave <c>E…</c>).
/// <c>prmake_epic_story_save</c> e <c>prmake_epic_story_convert</c> são ações (rodam como quem pediu —
/// <see cref="CaseSpecialistMiddleware.ActingTools"/>; a conversão usa a integração do DevOps dessa pessoa).
/// <para>0104: o Especialista do caso de um CARD do épico lê as histórias e, com a flag do épico ligada, escreve só RASCUNHOS
/// (<see cref="CardStoryAccess"/>); converter em card exige que quem pediu tenha o papel da Mesa (admin/management).</para>
/// </summary>
[McpServerToolType]
public class EpicStoryMcpTools(
    IEpicStoryApplication stories,
    EpicStoryConverter converter,
    IHttpContextAccessor http,
    solvace.timeline.application.Contracts.IUserRepository users,
    SpecialistActions specialist,
    IEpicApplication epics,
    DeskProfileService profile)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [McpServerTool(Name = "prmake_epic_stories", ReadOnly = true)]
    [Description("Historias e bugs escritos no PRMake dentro do epico (aba Historias da Mesa): #n, tipo (user-story|bug), titulo, status " +
                 "(draft|ready|converted|discarded), card do DevOps quando ja convertida, card de origem (sourceCard), prioridade, severidade, " +
                 "tags, campos. status = filtro (vazio = todas menos descartadas; all = todas). withText=true traz descricao/criterios/passos. " +
                 "Antes de escrever uma historia, confira aqui se ja existe. Especialista do caso de um card: so epicos em que o card esta " +
                 "(prmake_epics(card: <card>)).")]
    public Task<string> List(
        [Description("Numero do epico (#n) ou a chave E<n>")] string epic,
        string? status = null,
        [Description("Traz a descricao, os criterios de aceite e os passos de reproducao")] bool withText = false,
        CancellationToken ct = default) => Safe(async () =>
    {
        var n = EpicMcpTools.ParseNumber(epic);
        var access = await AccessAsync(n, ct);
        var list = await stories.ListAsync(n, status, ct);
        return Serialize(new
        {
            epic = n,
            key = EpicKey.Of(n),
            cardDrafts = access?.Note,
            total = list.Count,
            drafts = list.Count(s => s.Status is EpicStoryStatus.Draft or EpicStoryStatus.Ready),
            converted = list.Count(s => s.Status == EpicStoryStatus.Converted),
            stories = list.Take(200).Select(s => new
            {
                s.Number, s.Type, s.Title, s.Status, s.CardId, s.SourceCard, s.Converting, s.Priority, s.Severity,
                tags = s.Tags.Count > 0 ? s.Tags : null,
                fields = s.Fields.Count > 0 ? s.Fields : null,
                description = withText ? s.Description : null,
                acceptanceCriteria = withText ? s.AcceptanceCriteria : null,
                reproSteps = withText ? s.ReproSteps : null,
                s.CreatedByName, s.UpdatedAt, s.ConvertedAt, s.ConvertedByName
            })
        });
    });

    [McpServerTool(Name = "prmake_epic_story_save")]
    [Description("Cria (sem number) ou edita (com number) uma historia/bug do epico — RASCUNHO no PRMake, nao cria card. type = user-story | bug. " +
                 "User Story: descricao (o que e para quem/por que) + acceptanceCriteria claros e verificaveis (markdown, lista). Bug: descricao + " +
                 "reproSteps numerados com o resultado esperado x obtido. priority 1-4 (1 = mais alta), severity no texto do picklist do DevOps " +
                 "(ex.: '2 - High', so Bug), tags (lista), fields = {referenceName: valor} (System.AreaPath, System.IterationPath, " +
                 "System.AssignedTo — confira com prmake_devops_meta). status = draft | ready | discarded. Na edicao, campos nao enviados ficam " +
                 "como estao. Convertida nao se edita (edite o card no DevOps). Especialista do caso de um CARD: so em epico em que o card " +
                 "esta (criado ou vinculado) com rascunhos pelos cards permitidos (allowCardStoryDrafts); cria e edita SO rascunho (draft) " +
                 "e so os que o proprio card criou.")]
    public Task<string> Save(
        [Description("Numero do epico (#n) ou a chave E<n>")] string epic,
        [Description("Titulo (obrigatorio ao criar)")] string? title = null,
        [Description("Numero da historia (#n) para editar; vazio cria")] long? number = null,
        [Description("user-story | bug (padrao user-story ao criar)")] string? type = null,
        [Description("Descricao em markdown")] string? description = null,
        [Description("Criterios de aceite em markdown (User Story)")] string? acceptanceCriteria = null,
        [Description("Passos de reproducao em markdown (Bug)")] string? reproSteps = null,
        [Description("Prioridade (1 = mais alta)")] int? priority = null,
        [Description("Severidade do Bug (texto do picklist, ex.: '2 - High')")] string? severity = null,
        [Description("Tags (lista ou texto separado por virgula)")] JsonElement? tags = null,
        [Description("Campos do DevOps {referenceName: valor} (objeto JSON ou o JSON em texto)")] JsonElement? fields = null,
        [Description("draft | ready | discarded")] string? status = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var n = EpicMcpTools.ParseNumber(epic);
        var access = await AccessAsync(n, ct);
        var card = access?.Card;
        if (card is not null)
        {
            if (access!.WriteDenied is { } denied) throw new McpException(denied);
            if (CardStoryAccess.StatusDenied(status) is { } badStatus) throw new McpException(badStatus);
        }
        var actor = await ActorAsync(ct);
        var tagList = Tags(tags);
        var fieldMap = DevOpsMcpTools.FieldMap(fields);
        if (number is not { } storyNumber)
        {
            if (string.IsNullOrWhiteSpace(title)) throw new McpException("Informe o titulo da historia.");
            var created = await stories.CreateAsync(n, new SaveEpicStoryRequest
            {
                Type = type, Title = title, Description = description, AcceptanceCriteria = acceptanceCriteria, ReproSteps = reproSteps,
                Priority = priority, Severity = severity, Tags = tagList, Fields = fieldMap,
                Status = card is null ? status : EpicStoryStatus.Draft, SourceCard = card
            }, actor, ct);
            return Serialize(new { created = true, created.Number, link = Link(n, created.Number), story = created });
        }

        var current = await stories.GetAsync(storyNumber, ct);
        if (current.EpicNumber != n)
            throw new McpException($"A historia #{storyNumber} e do epico #{current.EpicNumber}, nao do #{n}.");
        if (card is not null && CardStoryAccess.EditDenied(current, card) is { } notOwn)
            throw new McpException(notOwn);
        var saved = await stories.UpdateAsync(storyNumber, new SaveEpicStoryRequest
        {
            Type = type,
            Title = title ?? current.Title,
            Description = description ?? current.Description,
            AcceptanceCriteria = acceptanceCriteria ?? current.AcceptanceCriteria,
            ReproSteps = reproSteps ?? current.ReproSteps,
            Priority = priority ?? current.Priority,
            Severity = severity ?? current.Severity,
            Tags = tagList ?? current.Tags,
            Fields = fieldMap ?? current.Fields,
            Status = status
        }, actor, ct);
        return Serialize(new { updated = true, saved.Number, link = Link(n, saved.Number), story = saved });
    });

    [McpServerTool(Name = "prmake_epic_story_convert", Destructive = true)]
    [Description("Converte a historia/bug do epico em card do DevOps — SO quando a pessoa pedir. Tipo e campos pelo DeskStoryFieldMap " +
                 "(descricao, criterios de aceite / passos em HTML, prioridade, severidade, tags, campos), card entra no epico (created), " +
                 "Timeline do card. column/lane iniciais opcionais (padrao: o time/board do epico). Uma vez so: ja convertida devolve o erro " +
                 "alreadyConverted com o card. Responda com o numero do card criado. Campo obrigatorio do DevOps sem valor (ex.: " +
                 "Custom.Module da User Story) volta como erro required_fields com os nomes — grave-o na historia (fields) ou mande em fields.")]
    public Task<string> Convert(
        [Description("Numero da historia (#n)")] long number,
        string? column = null, string? lane = null, string? team = null, string? board = null,
        [Description("Campos do DevOps {referenceName: valor} so para esta conversao (ex.: {\"Custom.Module\": \"Login\"})")] JsonElement? fields = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var story = await stories.GetAsync(number, ct);
        var access = await AccessAsync(story.EpicNumber, ct);
        if (access is { Writer: false })
            throw new McpException(access.NotWriter);
        // 0104: converter em card é da Mesa de gestão — quem pediu (ou quem chama) precisa do papel admin/management.
        var (who, roles) = await profile.RequesterAsync(User, ct);
        if (CardStoryAccess.ConvertDenied(roles, who) is { } noRole)
            throw new McpException(noRole);
        var actor = await ActorAsync(ct);
        var target = specialist.OwnCard ?? EpicKey.Of(story.EpicNumber);
        return await specialist.RunAsync(target, $"Converter a história #{number} em card", story.Title, async () =>
        {
            var result = await converter.ConvertAsync(number, new ConvertEpicStoryRequest { Column = column, Lane = lane, Team = team, Board = board, Fields = DevOpsMcpTools.FieldMap(fields) },
                actor, User, specialist.Via, ct);
            return (Serialize(Controllers.EpicStoriesController.ToResponse(result)), $"card #{result.Card.Id} — {result.Summary}");
        }, ct);
    });

    // ── Internos ─────────────────────────────────────────────────────────────────────────────────

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    private static string Link(long epic, long story) => $"auth/desk?epic={epic}&tab=stories&story={story}";

    /// <summary>
    /// Com o token do caso: o épico do caso (chave <c>E…</c>) sem restrição; o caso de um CARD só nos épicos em que o card está
    /// (0104 — ler sempre; escrever conforme <see cref="CardStoryAccess"/>). Fora do token do caso: null (sem restrição).
    /// </summary>
    private async Task<CardStoryAccess?> AccessAsync(long epic, CancellationToken ct)
    {
        if (specialist.OwnCard is not { } own) return null;
        if (EpicKey.TryParse(own, out var ownEpic))
        {
            if (ownEpic != epic)
                throw new McpException("O Especialista do epico so mexe nas historias do proprio epico.");
            return null;
        }
        var access = CardStoryAccess.Of(await epics.GetAsync(epic, ct), own);
        if (!access.InEpic)
            throw new McpException($"O card {access.Card} nao esta no epico #{epic} — historias so nos epicos do card (prmake_epics(card: {access.Card})).");
        return access;
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

    private static async Task<string> Safe(Func<Task<string>> body)
    {
        try
        {
            return await body();
        }
        catch (EpicStoryConflictException e)
        {
            throw new McpException(e.CardId is { } card ? $"{e.Code}: {e.Message} (card {card})" : $"{e.Code}: {e.Message}", e);
        }
        catch (Exception e) when (e is DomainException or ExecutionPlanNotFoundException or ExecutionForbiddenException
                                       or ExecutionPlanConcurrencyException or DevOpsActionException or PersonalIntegrationRequiredException
                                       or ArgumentException or InvalidOperationException)
        {
            throw new McpException(e.Message, e);
        }
    }
}
