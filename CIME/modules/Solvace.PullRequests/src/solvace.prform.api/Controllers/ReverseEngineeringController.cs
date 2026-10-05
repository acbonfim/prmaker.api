using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Responses;
using solvace.knowledge.domain.Reverse;

namespace solvace.prform.Controllers;

/// <summary>
/// Engenharia reversa por módulo (feature 0052): leitura e sessões (rascunhos) para qualquer usuário logado — quem roda a
/// skill no módulo; aprovar, publicar e configurar o módulo só os papéis de <c>ReverseEngineeringApproverRoles</c>.
/// Publicado, o documento vira a seção <c>re-*</c> do projeto na Base Solvace e os itens entram no índice por item.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ReverseEngineeringController(IReverseEngineeringApplication application, solvace.timeline.application.Contracts.IUserRepository users,
    Cime.BuildingBlocks.RealTime.IRealTimeNotifier realTime, solvace.executionplans.application.Contracts.IExecutionTimelineWriter timeline,
    ILogger<ReverseEngineeringController> logger) : ControllerBase
{
    /// <summary>Grupo do tempo real de um módulo (a tela do módulo assina) e o da lista de módulos.</summary>
    public static string Group(string moduleKey) => $"reverse:{moduleKey}";
    public const string ListGroup = "reverse";
    /// <summary>Evento: a revisão mudou (andamento, rascunho, envio, revisão, publicação) — payload = cabeça da revisão.</summary>
    public const string RevisionEvent = "reverseRevision";

    [HttpGet("settings")]
    public Task<ActionResult<ReverseSettingsResponse>> Settings(CancellationToken ct) =>
        Run<ReverseSettingsResponse>(async () => Ok(await application.GetSettingsAsync(Roles(), ct)));

    /// <summary>Tipos de documento com o modelo efetivo (o que a skill segue).</summary>
    [HttpGet("doc-types")]
    public Task<ActionResult<List<ReverseDocTypeResponse>>> DocTypes(CancellationToken ct) =>
        Run<List<ReverseDocTypeResponse>>(async () => Ok(await application.GetDocTypesAsync(ct)));

    [HttpGet("modules")]
    public Task<ActionResult<List<ReverseModuleSummaryResponse>>> Modules(CancellationToken ct) =>
        Run<List<ReverseModuleSummaryResponse>>(async () => Ok(await application.ListModulesAsync(ct)));

    [HttpGet("modules/{key}")]
    public Task<ActionResult<ReverseModuleResponse>> Module([FromRoute] string key, CancellationToken ct) =>
        Run<ReverseModuleResponse>(async () => Ok(await application.GetModuleAsync(key, Roles(), ct)));

    /// <summary>Fontes (repositório/pasta/papel), apelidos do campo Module do card e notas.</summary>
    [HttpPut("modules/{key}")]
    public Task<ActionResult<ReverseModuleResponse>> UpsertModule([FromRoute] string key, [FromBody] UpsertReverseModuleRequest request, CancellationToken ct) =>
        Run<ReverseModuleResponse>(async () => Ok(await application.UpsertModuleAsync(key, request, await ActorAsync(ct), Roles(), ct)));

    /// <summary>Termo sugerido pelo glossário (0053): alias (apelido do módulo) | keyword (palavra-chave) | dismiss.</summary>
    [HttpPost("modules/{key}/terms")]
    public Task<ActionResult<ReverseModuleResponse>> ResolveTerm([FromRoute] string key, [FromBody] ResolveReverseTermRequest request, CancellationToken ct) =>
        Run<ReverseModuleResponse>(async () => Ok(await application.ResolveTermAsync(key, request, await ActorAsync(ct), Roles(), ct)));

    [HttpGet("modules/{key}/docs/{doc}")]
    public Task<ActionResult<ReverseDocResponse>> Doc([FromRoute] string key, [FromRoute] string doc, CancellationToken ct) =>
        Run<ReverseDocResponse>(async () => Ok(await application.GetDocAsync(key, doc, ct)));

    /// <summary>Abre (ou retoma) a sessão do Claude para um documento e devolve o pacote (modelo, publicado, sugestões...).</summary>
    [HttpPost("modules/{key}/docs/{doc}/sessions")]
    public Task<ActionResult<ReverseSessionResponse>> StartSession([FromRoute] string key, [FromRoute] string doc,
        [FromBody] StartReverseSessionRequest? request, CancellationToken ct) =>
        Run<ReverseSessionResponse>(async () =>
        {
            var session = await application.StartSessionAsync(key, doc, request ?? new(), await ActorAsync(ct), Roles(), ct);
            await NotifyAsync(session.Revision);
            return Ok(session);
        });

    /// <summary>Checagem do documento sem gravar (a skill confere antes de enviar).</summary>
    [HttpPost("modules/{key}/lint")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public Task<ActionResult<ReverseLintResult>> Lint([FromRoute] string key, [FromBody] LintReverseDocumentRequest request, CancellationToken ct) =>
        Run<ReverseLintResult>(async () => Ok(await application.LintAsync(key, request, ct)));

    /// <summary>Revisões: status = open | pending (para aprovar) | all | lista separada por vírgula.</summary>
    [HttpGet("revisions")]
    public Task<ActionResult<List<ReverseRevisionHead>>> Revisions([FromQuery] string? module, [FromQuery] string? doc, [FromQuery] string? status, CancellationToken ct) =>
        Run<List<ReverseRevisionHead>>(async () => Ok(await application.ListRevisionsAsync(module, doc, status, ct)));

    [HttpGet("revisions/{id:guid}")]
    public Task<ActionResult<ReverseRevisionResponse>> Revision([FromRoute] Guid id, CancellationToken ct) =>
        Run<ReverseRevisionResponse>(async () => Ok(await application.GetRevisionAsync(id, Roles(), ct)));

    [HttpPut("revisions/{id:guid}")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public Task<ActionResult<ReverseRevisionResponse>> SaveRevision([FromRoute] Guid id, [FromBody] SaveReverseRevisionRequest request, CancellationToken ct) =>
        Run<ReverseRevisionResponse>(async () => Ok(await NotifyAsync(await application.SaveRevisionAsync(id, request, await ActorAsync(ct), Roles(), ct))));

    /// <summary>Andamento da sessão do Claude (etapa, atividade do momento, registro) — a tela mostra ao vivo.</summary>
    [HttpPost("revisions/{id:guid}/progress")]
    public Task<ActionResult<ReverseRevisionHead>> Progress([FromRoute] Guid id, [FromBody] ReverseProgressUpdate request, CancellationToken ct) =>
        Run<ReverseRevisionHead>(async () => Ok(await NotifyAsync(await application.ReportProgressAsync(id, request, await ActorAsync(ct), Roles(), ct))));

    /// <summary>Envia para revisão (a checagem estrutural barra erros).</summary>
    [HttpPost("revisions/{id:guid}/submit")]
    public Task<ActionResult<ReverseRevisionResponse>> Submit([FromRoute] Guid id, CancellationToken ct) =>
        Run<ReverseRevisionResponse>(async () => Ok(await NotifyAsync(await application.SubmitAsync(id, await ActorAsync(ct), Roles(), ct))));

    /// <summary>approve | changes (com nota) | discard.</summary>
    [HttpPost("revisions/{id:guid}/review")]
    public Task<ActionResult<ReverseRevisionResponse>> Review([FromRoute] Guid id, [FromBody] ReviewReverseRevisionRequest request, CancellationToken ct) =>
        Run<ReverseRevisionResponse>(async () => Ok(await NotifyAsync(await application.ReviewAsync(id, request, await ActorAsync(ct), Roles(), ct))));

    /// <summary>Publica a revisão aprovada na Base Solvace (seção re-*, índice por item, integrações no grafo).</summary>
    [HttpPost("revisions/{id:guid}/publish")]
    public Task<ActionResult<ReverseRevisionResponse>> Publish([FromRoute] Guid id, [FromBody] PublishReverseRevisionRequest? request, CancellationToken ct) =>
        Run<ReverseRevisionResponse>(async () =>
        {
            var actor = await ActorAsync(ct);
            var revision = await NotifyAsync(await application.PublishAsync(id, request ?? new(), actor, Roles(), ct));
            await TellCardsAsync(revision, actor, ct);
            return Ok(revision);
        });

    /// <summary>0053: a sugestão que veio de um card e foi aplicada/recusada fica registrada na Timeline do card.</summary>
    private async Task TellCardsAsync(ReverseRevisionResponse revision, string actor, CancellationToken ct)
    {
        foreach (var byCard in revision.ResolvedSuggestions.Where(s => !string.IsNullOrWhiteSpace(s.CardNumber)).GroupBy(s => s.CardNumber!))
        {
            var lines = byCard.Select(s => s.Decision == "applied"
                ? $"- ✅ aplicada{(s.Items.Count > 0 ? $" ({string.Join(", ", s.Items)})" : "")}{(s.Note is null ? "" : $": {s.Note}")}"
                : $"- ❌ recusada: {s.Note}");
            var markdown = $"**Engenharia reversa — {revision.ModuleName ?? revision.ModuleKey} ({revision.DocType})**: a sugestão deste card foi tratada "
                           + $"na revisão #{revision.Number}, publicada por {actor}.\n" + string.Join("\n", lines);
            try { await timeline.WriteAsync(byCard.Key, markdown, null, actor, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "Timeline do card {Card} não registrou a sugestão", byCard.Key); }
        }
    }

    // ── UI/UX ───────────────────────────────────────────────────────────────────────────────────

    [HttpPost("modules/{key}/assets/link")]
    public Task<ActionResult<ReverseAssetResponse>> AddLink([FromRoute] string key, [FromBody] CreateReverseAssetLinkRequest request, CancellationToken ct) =>
        Run<ReverseAssetResponse>(async () => Ok(await application.AddLinkAsync(key, request, await ActorAsync(ct), ct)));

    /// <summary>Arquivo de UI/UX (imagem, PDF, export do Figma) — multipart: file, title, notes, screens (separadas por vírgula).</summary>
    [HttpPost("modules/{key}/assets/file")]
    [RequestSizeLimit(ReverseAsset.MaxFileBytes + 1024 * 1024)]
    public Task<ActionResult<ReverseAssetResponse>> AddFile([FromRoute] string key, IFormFile file, [FromForm] string? title, [FromForm] string? notes,
        [FromForm] string? screens, CancellationToken ct) =>
        Run<ReverseAssetResponse>(async () =>
        {
            if (file is null || file.Length == 0) return BadRequest(new { error = "Envie o arquivo (campo file)." });
            if (file.Length > ReverseAsset.MaxFileBytes) return BadRequest(new { error = $"O arquivo pode ter no máximo {ReverseAsset.MaxFileBytes / 1024 / 1024} MB." });
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var contentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? ExecutionPlanController.ResolveContentType(file.FileName, "application/octet-stream")
                : file.ContentType;
            return Ok(await application.AddFileAsync(key, title ?? file.FileName, file.FileName, contentType, buffer.ToArray(), notes,
                screens?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), await ActorAsync(ct), ct));
        });

    [HttpGet("assets/{id:guid}/file")]
    public async Task<ActionResult> AssetFile([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            var (name, type, data) = await application.GetAssetFileAsync(id, ct);
            return File(data, type, name);
        }
        catch (KnowledgeNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpDelete("assets/{id:guid}")]
    public Task<ActionResult<object>> DeleteAsset([FromRoute] Guid id, CancellationToken ct) =>
        Run<object>(async () =>
        {
            await application.DeleteAssetAsync(id, await ActorAsync(ct), Roles(), ct);
            return NoContent();
        });

    // ── 0059: infra (AWS) do módulo ─────────────────────────────────────────────────────────────

    /// <summary>Último mapa de infra lido pela skill (<c>re.sh infra</c>); 204 = ainda não lido.</summary>
    [HttpGet("modules/{key}/infra")]
    public Task<ActionResult<ReverseInfraResponse>> Infra([FromRoute] string key, CancellationToken ct) =>
        Run<ReverseInfraResponse>(async () => await application.GetInfraAsync(key, ct) is { } infra ? (ActionResult)Ok(infra) : NoContent());

    [HttpPut("modules/{key}/infra")]
    public Task<ActionResult<ReverseInfraResponse>> UpsertInfra([FromRoute] string key, [FromBody] UpsertReverseInfraRequest request, CancellationToken ct) =>
        Run<ReverseInfraResponse>(async () => Ok(await application.UpsertInfraAsync(key, request, await ActorAsync(ct), ct)));

    // ── 0054: armadilhas, sugestões por item, divergências com o KC ─────────────────────────────

    [HttpGet("traps")]
    public Task<ActionResult<List<ReverseTrapResponse>>> Traps([FromQuery] string? module, CancellationToken ct) =>
        Run<List<ReverseTrapResponse>>(async () => Ok(await application.ListTrapsAsync(module, ct)));

    /// <summary>Armadilhas novas (lote) ligadas a itens. Quem não aprova — e toda migração automática — cria "a conferir".</summary>
    [HttpPost("modules/{key}/traps")]
    public Task<ActionResult<List<ReverseTrapResponse>>> CreateTraps([FromRoute] string key, [FromBody] List<CreateReverseTrapRequest> request, CancellationToken ct) =>
        Run<List<ReverseTrapResponse>>(async () => Ok(await application.CreateTrapsAsync(key, request, await ActorAsync(ct), Roles(), ct)));

    [HttpPut("traps/{id:guid}")]
    public Task<ActionResult<ReverseTrapResponse>> UpdateTrap([FromRoute] Guid id, [FromBody] UpdateReverseTrapRequest request, CancellationToken ct) =>
        Run<ReverseTrapResponse>(async () => Ok(await application.UpdateTrapAsync(id, request, await ActorAsync(ct), Roles(), ct)));

    [HttpDelete("traps/{id:guid}")]
    public Task<ActionResult<object>> DeleteTrap([FromRoute] Guid id, CancellationToken ct) =>
        Run<object>(async () =>
        {
            await application.DeleteTrapAsync(id, await ActorAsync(ct), Roles(), ct);
            return NoContent();
        });

    /// <summary>A skill ligou as armadilhas antigas e as sugestões aos itens — a seção antiga sai do espelho.</summary>
    [HttpPost("modules/{key}/traps/migrated")]
    public Task<ActionResult<ReverseModuleResponse>> TrapsMigrated([FromRoute] string key, CancellationToken ct) =>
        Run<ReverseModuleResponse>(async () => Ok(await application.MarkTrapsMigratedAsync(key, await ActorAsync(ct), Roles(), ct)));

    /// <summary>Sugestão → armadilha (aprovador).</summary>
    [HttpPost("suggestions/{id:guid}/to-trap")]
    public Task<ActionResult<ReverseTrapResponse>> SuggestionToTrap([FromRoute] Guid id, [FromBody] CreateReverseTrapRequest? request, CancellationToken ct) =>
        Run<ReverseTrapResponse>(async () => Ok(await application.SuggestionToTrapAsync(id, request?.Title, await ActorAsync(ct), Roles(), ct)));

    /// <summary>Liga a sugestão a um item/documento da engenharia reversa (migração, análises).</summary>
    [HttpPost("suggestions/{id:guid}/item")]
    public Task<ActionResult<ArchitectureSuggestionResponse>> LinkSuggestion([FromRoute] Guid id, [FromBody] LinkSuggestionItemRequest request, CancellationToken ct) =>
        Run<ArchitectureSuggestionResponse>(async () => Ok(await application.LinkSuggestionAsync(id, request, ct)));

    /// <summary>Divergências entre o Knowledge Center e o código (para o time de produto).</summary>
    [HttpGet("kc-divergences")]
    public Task<ActionResult<List<ArchitectureSuggestionResponse>>> KcDivergences(CancellationToken ct) =>
        Run<List<ArchitectureSuggestionResponse>>(async () => Ok(await application.KcDivergencesAsync(ct)));

    // ── Índice por item ─────────────────────────────────────────────────────────────────────────

    /// <summary>Busca nos itens publicados (q vazio = lista filtrada). module/kind aceitam vários (separados por vírgula).</summary>
    [HttpGet("index/search")]
    public Task<ActionResult<List<ReverseIndexHit>>> Search([FromQuery] string? q, [FromQuery] string? module, [FromQuery] string? kind, [FromQuery] string? doc,
        [FromQuery] int limit = 30, [FromQuery] bool removed = false, [FromQuery] string? card = null, CancellationToken ct = default) =>
        Run<List<ReverseIndexHit>>(async () =>
        {
            var hits = await application.SearchAsync(q, Split(module), Split(kind), doc, limit <= 0 ? 30 : limit, removed, ct);
            if (!string.IsNullOrWhiteSpace(card)) await application.RecordConsultedAsync(card, hits.Take(5).Select(h => h.Ref), ct);
            return Ok(hits);
        });

    /// <summary>Um ou mais itens com o texto inteiro: refs = módulo#RN-012 (vírgula separa); module = padrão para ID sem módulo.</summary>
    [HttpGet("items")]
    public Task<ActionResult<List<ReverseItemResponse>>> Items([FromQuery] string refs, [FromQuery] string? module, [FromQuery] string? card, CancellationToken ct) =>
        Run<List<ReverseItemResponse>>(async () =>
        {
            var items = await application.GetItemsAsync(Split(refs) ?? [], module, ct);
            if (!string.IsNullOrWhiteSpace(card)) await application.RecordConsultedAsync(card, items.Select(i => i.Ref), ct);
            return Ok(items);
        });

    /// <summary>Impacto entre módulos: itens que citam a tabela, o item, o módulo ou o termo.</summary>
    [HttpGet("impact")]
    public Task<ActionResult<List<ReverseIndexHit>>> Impact([FromQuery] string term, [FromQuery] int limit = 100, CancellationToken ct = default) =>
        Run<List<ReverseIndexHit>>(async () => Ok(await application.ImpactAsync(term, limit, ct)));

    // ── Card (analisar-bug) ─────────────────────────────────────────────────────────────────────

    /// <summary>Texto compacto para o contexto da análise: módulos do card, estado da engenharia reversa e os itens que casam.</summary>
    [HttpGet("for-card/{card}")]
    public async Task<ActionResult> ForCard([FromRoute] string card, [FromQuery] string? module, [FromQuery] string? q, CancellationToken ct)
    {
        try { return Content(await application.ForCardAsync(card, module, q, ct), "text/plain; charset=utf-8"); }
        catch (DomainException e) { return BadRequest(new { error = e.Message }); }
    }

    /// <summary>A análise consultou estes itens (kb.sh re get com card) — conta para a trava da investigação.</summary>
    [HttpPost("consulted")]
    public Task<ActionResult<object>> Consulted([FromBody] ReverseConsultedRequest request, CancellationToken ct) =>
        Run<object>(async () =>
        {
            await application.RecordConsultedAsync(request.Card, request.Refs, ct);
            return Ok(new { ok = true });
        });

    /// <summary>Avisa a tela do módulo e a lista (sem o conteúdo do documento). Falha no tempo real não derruba a gravação.</summary>
    private async Task<T> NotifyAsync<T>(T revision) where T : ReverseRevisionHead
    {
        try
        {
            var head = new ReverseRevisionHead
            {
                Id = revision.Id, ModuleKey = revision.ModuleKey, ModuleName = revision.ModuleName, DocType = revision.DocType, Number = revision.Number,
                Mode = revision.Mode, Status = revision.Status, Summary = revision.Summary, CoverageRatio = revision.CoverageRatio, Length = revision.Length,
                CreatedAt = revision.CreatedAt, CreatedBy = revision.CreatedBy, UpdatedAt = revision.UpdatedAt, UpdatedBy = revision.UpdatedBy,
                SubmittedAt = revision.SubmittedAt, ReviewedBy = revision.ReviewedBy, PublishedAt = revision.PublishedAt, PublishedBy = revision.PublishedBy,
                ReviewNote = revision.ReviewNote, Progress = revision.Progress, ProgressAt = revision.ProgressAt
            };
            await realTime.NotifyGroupAsync(Group(revision.ModuleKey), RevisionEvent, head);
            await realTime.NotifyGroupAsync(ListGroup, RevisionEvent, new { head.Id, head.ModuleKey, head.DocType, head.Number, head.Status, head.ProgressAt });
        }
        catch (Exception e) { logger.LogWarning(e, "Tempo real da engenharia reversa falhou"); }
        return revision;
    }

    private static List<string>? Split(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Papéis de quem chama (claim role do JWT).</summary>
    private List<string> Roles() =>
        User.FindAll(ClaimTypes.Role).Concat(User.FindAll("role")).Select(c => c.Value).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private Task<string> ActorAsync(CancellationToken ct) => KnowledgeController.ActorAsync(users, User, ct);

    private async Task<ActionResult<T>> Run<T>(Func<Task<ActionResult>> action)
    {
        try { return await action(); }
        catch (DomainException e) { return BadRequest(new { error = e.Message }); }
        catch (KnowledgeNotFoundException e) { return NotFound(new { error = e.Message }); }
        catch (ReverseForbiddenException e) { return StatusCode(StatusCodes.Status403Forbidden, new { error = e.Message }); }
    }
}
