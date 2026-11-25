using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.azure.domain.Exceptions;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;

namespace solvace.prform.Controllers;

/// <summary>
/// 0099 (ajustes 2): histórias e bugs escritos no PRMake dentro do épico (aba "Histórias" da Mesa de gestão) — rascunho que vira card
/// do DevOps uma vez só (<c>POST EpicStories/{n}/convert</c>, com a integração de quem age). Rotas do épico em
/// <c>Epics/{n}/stories</c> (rota absoluta aqui, para não misturar com o <see cref="EpicsController"/>).
/// <para>Acesso: leitura para quem está logado (o <c>#n</c> da Bancada mostra a história); gravação só com o papel <c>admin</c> ou
/// <c>management</c>. Token do caso (especialista de gestão): só lê, e só o próprio épico — as gravações dele vão pelo MCP
/// <c>prmake_epic_story_*</c>.</para>
/// Erros: 400/403/404 <c>{error}</c>; 409 <c>{code, error, cardId?}</c> com <c>code</c> <c>alreadyConverted</c> | <c>notEditable</c> |
/// <c>converting</c>; recusa do DevOps na conversão = <c>{code, message, error}</c> com o status do DevOps (como o DevOpsController);
/// DevOps fora do ar = 502 <c>{code: "devops_error"}</c>.
/// Tempo real: <c>epicsUpdated</c> <c>{number, key, action: "stories", story}</c> no grupo <c>epics</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class EpicStoriesController(IEpicStoryApplication stories, solvace.timeline.application.Contracts.IUserRepository users)
    : ExecutionControllerBase(users)
{
    /// <summary>Papéis que gravam na Mesa de gestão (ajustes 2 — "Regras de acesso").</summary>
    public static readonly IReadOnlyList<string> DeskRoles = ["admin", "management"];

    /// <summary>Histórias do épico, mais novas primeiro. <c>status</c>: vazio = todas menos descartadas; <c>all</c> = todas; ou a lista (vírgula).</summary>
    [HttpGet("/api/v{version:apiVersion}/Epics/{epic:long}/stories")]
    public Task<ActionResult<IReadOnlyList<EpicStoryResponse>>> List([FromRoute] long epic, [FromQuery] string? status, CancellationToken ct) =>
        Go<IReadOnlyList<EpicStoryResponse>>(async () =>
        {
            EnsureSpecialistEpic(epic);
            return Ok(await stories.ListAsync(epic, status, ct));
        });

    /// <summary>Cria a história/bug no épico (201). Número da sequência global (<c>#n</c>).</summary>
    [HttpPost("/api/v{version:apiVersion}/Epics/{epic:long}/stories")]
    public Task<ActionResult<EpicStoryResponse>> Create([FromRoute] long epic, [FromBody] SaveEpicStoryRequest request, CancellationToken ct) =>
        Go<EpicStoryResponse>(async () =>
        {
            EnsureDeskWriter();
            return StatusCode(StatusCodes.Status201Created, await stories.CreateAsync(epic, request, await GetActorAsync(ct), ct));
        });

    [HttpGet("{number:long}")]
    public Task<ActionResult<EpicStoryResponse>> Get([FromRoute] long number, CancellationToken ct) =>
        Go<EpicStoryResponse>(async () =>
        {
            var story = await stories.GetAsync(number, ct);
            EnsureSpecialistEpic(story.EpicNumber);
            return Ok(story);
        });

    /// <summary>Edita (formulário inteiro: null limpa; <c>type</c>/<c>status</c> null não mexem). Convertida → 409 <c>notEditable</c>.</summary>
    [HttpPut("{number:long}")]
    public Task<ActionResult<EpicStoryResponse>> Update([FromRoute] long number, [FromBody] SaveEpicStoryRequest request, CancellationToken ct) =>
        Go<EpicStoryResponse>(async () =>
        {
            EnsureDeskWriter();
            return Ok(await stories.UpdateAsync(number, request, await GetActorAsync(ct), ct));
        });

    /// <summary>Descarta (status <c>discarded</c>; idempotente — volta com PUT <c>status: "draft"</c>). Convertida → 409 <c>notEditable</c>.</summary>
    [HttpDelete("{number:long}")]
    public Task<ActionResult<EpicStoryResponse>> Discard([FromRoute] long number, CancellationToken ct) =>
        Go<EpicStoryResponse>(async () =>
        {
            EnsureDeskWriter();
            return Ok(await stories.DiscardAsync(number, await GetActorAsync(ct), ct));
        });

    /// <summary>
    /// Converte em card do DevOps (tipo e campos pelo <c>DeskStoryFieldMap</c>; markdown → HTML), põe o card no épico (<c>created</c>) e
    /// marca a história <c>converted</c>. Corpo opcional <c>{team?, board?, column?, lane?}</c> (padrão: time/board do épico). Já
    /// convertida → 409 <c>{code: "alreadyConverted", cardId}</c>. Resposta: <c>{story, card: {id, type, title, state, url}, summary}</c>.
    /// </summary>
    [HttpPost("{number:long}/convert")]
    public Task<ActionResult<object>> Convert([FromRoute] long number, [FromBody] ConvertEpicStoryRequest? request,
        [FromServices] EpicStoryConverter converter, CancellationToken ct) =>
        Go<object>(async () =>
        {
            EnsureDeskWriter();
            var result = await converter.ConvertAsync(number, request, await GetActorAsync(ct), User, null, ct);
            return Ok(ToResponse(result));
        });

    /// <summary>Resposta da conversão (REST e MCP).</summary>
    public static object ToResponse(EpicStoryConversionResult result) => new
    {
        story = result.Story,
        card = new { result.Card.Id, result.Card.Type, result.Card.Title, result.Card.State, result.Card.Url },
        summary = result.Summary
    };

    // ── acesso e erros ───────────────────────────────────────────────────────────────────────

    /// <summary>Gravação: só <c>admin</c>/<c>management</c> (o token do caso não grava pelo REST — o middleware já barra).</summary>
    private void EnsureDeskWriter()
    {
        if (CaseClaims.SpecialistOf(User) is not null)
            throw new ExecutionForbiddenException("O especialista grava histórias pelo MCP (prmake_epic_story_save / prmake_epic_story_convert).");
        if (!DeskRoles.Any(User.IsInRole))
            throw new ExecutionForbiddenException("Histórias da Mesa de gestão: só quem tem o papel admin ou management grava.");
    }

    /// <summary>Com o token do caso: o épico precisa ser o do caso.</summary>
    private void EnsureSpecialistEpic(long epic)
    {
        if (CaseClaims.SpecialistOf(User) is not null && !(EpicKey.TryParse(CaseClaims.CardOf(User), out var own) && own == epic))
            throw new ExecutionForbiddenException("O token do caso só vale para o épico do caso.");
    }

    /// <summary>Erros do <see cref="ExecutionControllerBase"/> + os 409 da história com <c>code</c> e a recusa do DevOps na conversão.</summary>
    private Task<ActionResult<T>> Go<T>(Func<Task<ActionResult>> body) => Run<T>(async () =>
    {
        try
        {
            return await body();
        }
        catch (EpicStoryConflictException e)
        {
            return Conflict(new { code = e.Code, error = e.Message, message = e.Message, cardId = e.CardId });
        }
        catch (DevOpsActionException e)
        {
            var code = e is DevOpsDeskException desk ? desk.Code : e.StatusCode switch
            {
                404 => "not_found",
                409 => "conflict",
                400 => "invalid",
                _ => "devops_error"
            };
            return StatusCode(e.StatusCode, new { code, message = e.Message, error = e.Message });
        }
        catch (HttpRequestException e)
        {
            // DevOps fora do ar na conversão: a trava já foi solta (a história continua editável) — 502 em vez de 500 genérico.
            var message = $"O DevOps não respondeu: {e.Message}";
            return StatusCode(StatusCodes.Status502BadGateway, new { code = "devops_error", message, error = message });
        }
    });
}
