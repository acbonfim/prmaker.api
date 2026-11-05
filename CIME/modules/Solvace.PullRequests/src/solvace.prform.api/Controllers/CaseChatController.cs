using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;

namespace solvace.prform.Controllers;

/// <summary>
/// 0080: Especialista do caso — a conversa do card entre as pessoas e um agente próprio (pista do especialista no
/// executor). Pessoas escrevem (texto, imagens coladas, arquivos, voz), decidem propostas e confirmam ações; o especialista
/// (token do caso) lê o contexto, responde, fixa pontos, propõe a nova versão do plano e pede confirmação — sem alterar o
/// plano (<see cref="CaseSpecialistMiddleware"/>).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class CaseChatController(CaseChatApplication chat, IExecutionQueueApplication queue,
    solvace.timeline.application.Contracts.IUserRepository users) : ExecutionControllerBase(users)
{
    private const long MaxMessageRequestBytes = 60L * 1024 * 1024;
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private readonly solvace.timeline.application.Contracts.IUserRepository _users = users;

    [HttpGet("{card}")]
    public Task<ActionResult<CaseChatResponse>> Get([FromRoute] string card, [FromQuery] int? limit, CancellationToken ct) =>
        Run<CaseChatResponse>(async () => Ok(await chat.GetAsync(card, await ActorAsync(ct), limit, ct)));

    /// <summary>Mensagem de uma pessoa (multipart: text, transcript, voice, peopleOnly, deep, replyTo, files).</summary>
    [HttpPost("{card}/messages")]
    [RequestSizeLimit(MaxMessageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxMessageRequestBytes)]
    public Task<ActionResult<CaseMessageResponse>> Post([FromRoute] string card, [FromForm] string? text, [FromForm] string? transcript,
        [FromForm] bool voice, [FromForm] bool peopleOnly, [FromForm] bool deep, [FromForm] int? replyTo, [FromForm] List<IFormFile>? files,
        CancellationToken ct) =>
        Run<CaseMessageResponse>(async () =>
        {
            var uploads = await ReadUploadsAsync(files, ct);
            var request = new PostCaseMessageRequest { Text = text, Transcript = transcript, Voice = voice, PeopleOnly = peopleOnly, Deep = deep, ReplyTo = replyTo };
            return Ok(await chat.PostAsync(card, request, uploads, await ActorAsync(ct), ct));
        });

    [HttpPatch("{card}/messages/{id:guid}")]
    public Task<ActionResult<CaseMessageResponse>> Edit([FromRoute] string card, [FromRoute] Guid id, [FromBody] EditCaseMessageRequest request, CancellationToken ct) =>
        Run<CaseMessageResponse>(async () => Ok(await chat.EditAsync(card, id, request.Text, await ActorAsync(ct), ct)));

    [HttpDelete("{card}/messages/{id:guid}")]
    public Task<ActionResult> Delete([FromRoute] string card, [FromRoute] Guid id, CancellationToken ct) =>
        RunPlain(async () =>
        {
            await chat.DeleteAsync(card, id, await ActorAsync(ct), ct);
            return NoContent();
        });

    /// <summary>"Levar para o plano": a mensagem vira um comentário-instrução no plano atual.</summary>
    [HttpPost("{card}/messages/{id:guid}/to-plan")]
    public Task<ActionResult<ExecutionNoteResponse>> ToPlan([FromRoute] string card, [FromRoute] Guid id, [FromBody] EditCaseMessageRequest? request, CancellationToken ct) =>
        Run<ExecutionNoteResponse>(async () => Ok(await chat.ToPlanAsync(card, id, request?.Text, await ActorAsync(ct), ct)));

    /// <summary>0090 (<c>/clear</c>): limpa a conversa para todos e o especialista recomeça do zero (nada sai do banco).</summary>
    [HttpPost("{card}/clear")]
    public Task<ActionResult> Clear([FromRoute] string card, CancellationToken ct) =>
        RunPlain(async () =>
        {
            await chat.ClearAsync(card, await ActorAsync(ct), ct);
            return NoContent();
        });

    /// <summary>0090 (<c>/cancelar-item-de-trabalho #n</c>): cancela (e, com <c>undo</c>, desfaz) um item deste card e avisa na conversa.</summary>
    [HttpPost("{card}/work-items/{number:long}/cancel")]
    public Task<ActionResult<WorkItemResponse>> CancelWorkItem([FromRoute] string card, [FromRoute] long number,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CancelWorkItemRequest? request, CancellationToken ct) =>
        Run<WorkItemResponse>(async () =>
            Ok(await chat.CancelWorkItemAsync(card, number, request?.Undo ?? false, request?.Reason, await ActorAsync(ct), ct)));

    [HttpPost("{card}/typing")]
    public Task<ActionResult> Typing([FromRoute] string card, CancellationToken ct) =>
        RunPlain(async () =>
        {
            await chat.TypingAsync(card, await ActorAsync(ct), ct);
            return NoContent();
        });

    /// <summary>Chama o especialista agora (tentar de novo, investigar a fundo).</summary>
    [HttpPost("{card}/ask")]
    public Task<ActionResult<ExecutionRequestResponse?>> Ask([FromRoute] string card, [FromQuery] bool deep, CancellationToken ct) =>
        Run<ExecutionRequestResponse?>(async () => Ok(await chat.AskAsync(card, deep, await ActorAsync(ct), ct)));

    /// <summary>Para a resposta do especialista em andamento.</summary>
    [HttpPost("{card}/run/cancel")]
    public Task<ActionResult<ExecutionRequestResponse>> CancelRun([FromRoute] string card, CancellationToken ct) =>
        Run<ExecutionRequestResponse>(async () =>
        {
            var run = await queue.GetSpecialistRunAsync(card, ct);
            if (run is null || run.Status is not ("queued" or "claimed" or "running"))
                return NoContent();
            return Ok(await queue.CancelAsync(run.Id, "Parado na conversa do caso", await GetActorAsync(ct), ct));
        });

    [HttpGet("{card}/files/{id:guid}/content")]
    public Task<ActionResult> FileContent([FromRoute] string card, [FromRoute] Guid id, [FromQuery] bool download, CancellationToken ct) =>
        RunPlain(async () =>
        {
            var (file, data) = await chat.GetFileAsync(card, id, ct);
            return download ? File(data, file.ContentType, file.Name) : File(data, file.ContentType);
        });

    /// <summary>Arquivo pelo número (#n) — o especialista baixa para ler (prmake-case.sh file).</summary>
    [HttpGet("{card}/files/number/{number:int}/content")]
    public Task<ActionResult> FileByNumber([FromRoute] string card, [FromRoute] int number, CancellationToken ct) =>
        RunPlain(async () =>
        {
            var (file, data) = await chat.GetFileByNumberAsync(card, number, ct);
            Response.Headers["X-Case-File-Name"] = Uri.EscapeDataString(file.Name);
            return File(data, file.ContentType, file.Name);
        });

    /// <summary>Arquivo solto (gerado pelo especialista) — entra numa resposta pelo número.</summary>
    [HttpPost("{card}/files")]
    [RequestSizeLimit(MaxMessageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxMessageRequestBytes)]
    public Task<ActionResult<CaseFileResponse>> Upload([FromRoute] string card, [FromForm] IFormFile? file, CancellationToken ct) =>
        Run<CaseFileResponse>(async () =>
        {
            if (file is null) return BadRequest(new { error = "Envie o arquivo (campo file)." });
            var upload = (await ReadUploadsAsync([file], ct))[0];
            return Ok(await chat.UploadAsync(card, upload, await ActorAsync(ct), ct));
        });

    [HttpPost("{card}/points")]
    public Task<ActionResult<CasePointResponse>> AddPoint([FromRoute] string card, [FromBody] CasePointRequest request, CancellationToken ct) =>
        Run<CasePointResponse>(async () => Ok(await chat.AddPointAsync(card, request, await ActorAsync(ct), ct)));

    [HttpPatch("{card}/points/{id:guid}")]
    public Task<ActionResult<CasePointResponse>> UpdatePoint([FromRoute] string card, [FromRoute] Guid id, [FromBody] CasePointRequest request, CancellationToken ct) =>
        Run<CasePointResponse>(async () => Ok(await chat.UpdatePointAsync(card, id, request, await ActorAsync(ct), ct)));

    [HttpPost("{card}/proposals")]
    public Task<ActionResult<CaseProposalResponse>> Propose([FromRoute] string card, [FromBody] CaseProposalRequest request, CancellationToken ct) =>
        Run<CaseProposalResponse>(async () => Ok(await chat.ProposeAsync(card, request, await ActorAsync(ct), ct)));

    [HttpPost("{card}/proposals/{id:guid}/decision")]
    public Task<ActionResult<CaseProposalResponse>> DecideProposal([FromRoute] string card, [FromRoute] Guid id, [FromBody] CaseProposalDecisionRequest request,
        CancellationToken ct) =>
        Run<CaseProposalResponse>(async () => Ok(await chat.DecideProposalAsync(card, id, request, await ActorAsync(ct), ct)));

    [HttpPost("{card}/actions")]
    public Task<ActionResult<CaseActionResponse>> RequestAction([FromRoute] string card, [FromBody] CaseActionRequest request, CancellationToken ct) =>
        Run<CaseActionResponse>(async () => Ok(await chat.RequestActionAsync(card, request, await ActorAsync(ct), ct)));

    [HttpPost("{card}/actions/{id:guid}/decision")]
    public Task<ActionResult<CaseActionResponse>> DecideAction([FromRoute] string card, [FromRoute] Guid id, [FromBody] CaseActionDecisionRequest request,
        CancellationToken ct) =>
        Run<CaseActionResponse>(async () => Ok(await chat.DecideActionAsync(card, id, request.Confirm, await ActorAsync(ct), ct)));

    [HttpPost("{card}/actions/{id:guid}/finish")]
    public Task<ActionResult<CaseActionResponse>> FinishAction([FromRoute] string card, [FromRoute] Guid id, [FromBody] CaseActionFinishRequest request,
        CancellationToken ct) =>
        Run<CaseActionResponse>(async () => Ok(await chat.FinishActionAsync(card, id, request, await ActorAsync(ct), ct)));

    // ── Especialista (token do caso) ─────────────────────────────────────────────────────────────

    [HttpGet("{card}/context")]
    public Task<ActionResult> Context([FromRoute] string card, CancellationToken ct) =>
        RunPlain(async () => Content(await chat.ContextAsync(card, ct), "text/markdown; charset=utf-8"));

    [HttpGet("{card}/messages/new")]
    public Task<ActionResult<List<CaseMessageResponse>>> NewMessages([FromRoute] string card, [FromQuery] int? after, CancellationToken ct) =>
        Run<List<CaseMessageResponse>>(async () => Ok(await chat.MessagesForSpecialistAsync(card, after, await ActorAsync(ct), ct)));

    [HttpPost("{card}/reply")]
    public Task<ActionResult<CaseMessageResponse>> Reply([FromRoute] string card, [FromBody] CaseReplyRequest request, CancellationToken ct) =>
        Run<CaseMessageResponse>(async () => Ok(await chat.ReplyAsync(card, request, await ActorAsync(ct), null, ct)));

    // ── Internos ─────────────────────────────────────────────────────────────────────────────────

    private async Task<CaseActor> ActorAsync(CancellationToken ct)
    {
        if (CaseClaims.SpecialistOf(User) is { } specialist)
            return specialist;
        var userId = CurrentUserId;
        string? name = null;
        if (userId.HasValue)
            name = await _users.GetFullNameAsync(userId.Value, ct);
        name ??= User.FindFirst(ClaimTypes.Name)?.Value;
        return new CaseActor(userId, string.IsNullOrWhiteSpace(name) ? "Usuário" : name.Trim(), User.IsInRole("admin") || User.IsInRole("gestor"));
    }

    private static async Task<List<ExecutionArtifactUpload>> ReadUploadsAsync(List<IFormFile>? files, CancellationToken ct)
    {
        var uploads = new List<ExecutionArtifactUpload>();
        foreach (var f in files ?? [])
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            var contentType = !string.IsNullOrWhiteSpace(f.ContentType) && f.ContentType != "application/octet-stream"
                ? f.ContentType
                : ContentTypes.TryGetContentType(f.FileName, out var ctype) ? ctype : "application/octet-stream";
            uploads.Add(new ExecutionArtifactUpload(string.IsNullOrWhiteSpace(f.FileName) ? "arquivo" : f.FileName, null, null, null, contentType, ms.ToArray()));
        }
        return uploads;
    }
}
