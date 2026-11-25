using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;

namespace solvace.prform.Controllers;

/// <summary>
/// 0099: épicos da Mesa de gestão. Os itens, a conversa do especialista, os arquivos e os relatados do épico usam as rotas de
/// card com a chave <c>E{número}</c> (<see cref="EpicKey"/>). Tempo real: <c>epicsUpdated</c> no grupo <c>epics</c>.
/// Token do caso: só lê, e só o próprio épico (as gravações do especialista vão pelo MCP <c>prmake_epic*</c>).
/// Ajustes 2: gravar exige <c>admin</c>/<c>management</c> (<see cref="DeskAccessAttribute"/>); ler (lista, épico, tags,
/// <c>by-card</c> dos chips da Bancada) e o acompanhamento seguem para todo usuário logado.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class EpicsController(IEpicApplication epics, solvace.timeline.application.Contracts.IUserRepository users)
    : ExecutionControllerBase(users)
{
    /// <summary>
    /// Épicos, mais recentes primeiro. <c>status</c>: um ou vários (vírgula); vazio = todos menos arquivados; <c>all</c> =
    /// todos. <c>q</c>: parte do título, tag, número (<c>123</c>/<c>E123</c>) ou card do DevOps que representa o épico.
    /// <c>mine=true</c>: só os que eu criei.
    /// </summary>
    [HttpGet]
    public Task<ActionResult<IReadOnlyList<EpicSummary>>> List([FromQuery] string? status, [FromQuery] string? q, [FromQuery] bool mine,
        CancellationToken ct) =>
        Run<IReadOnlyList<EpicSummary>>(async () =>
        {
            var list = await epics.ListAsync(status, q, mine ? RequireUserId() : null, ct);
            if (CaseClaims.SpecialistOf(User) is not null)
                list = list.Where(e => IsOwnEpic(e.Number)).ToList();
            return Ok(list);
        });

    /// <summary>Ajustes 1: tags usadas nos épicos (todas as situações), mais usadas primeiro, depois pelo nome; <c>q</c> filtra (contém).</summary>
    [HttpGet("tags")]
    public Task<ActionResult<IReadOnlyList<string>>> Tags([FromQuery] string? q, CancellationToken ct) =>
        Run<IReadOnlyList<string>>(async () => Ok(await epics.TagsAsync(q, ct)));

    [HttpGet("{number:long}")]
    public Task<ActionResult<EpicResponse>> Get([FromRoute] long number, CancellationToken ct) =>
        Run<EpicResponse>(async () =>
        {
            EnsureSpecialistEpic(number);
            return Ok(await epics.GetAsync(number, ct));
        });

    /// <summary>Cria o épico (201). Número da sequência global dos itens.</summary>
    [DeskAccess]
    [HttpPost]
    public Task<ActionResult<EpicResponse>> Create([FromBody] SaveEpicRequest request, CancellationToken ct) =>
        Run<EpicResponse>(async () =>
            StatusCode(StatusCodes.Status201Created, await epics.CreateAsync(request, await GetActorAsync(ct), ct)));

    [DeskAccess]
    [HttpPut("{number:long}")]
    public Task<ActionResult<EpicResponse>> Update([FromRoute] long number, [FromBody] SaveEpicRequest request, CancellationToken ct) =>
        Run<EpicResponse>(async () =>
        {
            EnsureSpecialistEpic(number);
            return Ok(await epics.UpdateAsync(number, request, await GetActorAsync(ct), ct));
        });

    /// <summary><c>{status}</c>: active | paused | done | archived.</summary>
    [DeskAccess]
    [HttpPost("{number:long}/status")]
    public Task<ActionResult<EpicResponse>> SetStatus([FromRoute] long number, [FromBody] EpicStatusRequest request, CancellationToken ct) =>
        Run<EpicResponse>(async () =>
        {
            EnsureSpecialistEpic(number);
            return Ok(await epics.SetStatusAsync(number, request?.Status, await GetActorAsync(ct), ct));
        });

    /// <summary>
    /// Ajustes 1: grava os filtros do board do épico (corpo = <c>{lanes, types, states, assignedTo, tags, text}</c>; lane padrão =
    /// <c>""</c>) — a tela grava com debounce e o especialista usa no board do time. <c>epicsUpdated</c> action <c>filters</c>.
    /// </summary>
    [DeskAccess]
    [HttpPut("{number:long}/board-filters")]
    public Task<ActionResult<EpicResponse>> SetBoardFilters([FromRoute] long number, [FromBody] EpicBoardFilters? request, CancellationToken ct) =>
        Run<EpicResponse>(async () =>
        {
            EnsureSpecialistEpic(number);
            return Ok(await epics.SetBoardFiltersAsync(number, request, await GetActorAsync(ct), ct));
        });

    /// <summary>Põe um card do DevOps no épico (<c>role</c> padrão <c>linked</c>; papel só sobe).</summary>
    [DeskAccess]
    [HttpPost("{number:long}/cards")]
    public Task<ActionResult<EpicCardResponse>> AddCard([FromRoute] long number, [FromBody] AddEpicCardRequest request, CancellationToken ct) =>
        Run<EpicCardResponse>(async () =>
        {
            EnsureSpecialistEpic(number);
            return Ok(await epics.AddCardAsync(number, request, await GetActorAsync(ct), ct));
        });

    /// <summary>Tira o card do épico (não mexe no card). Idempotente.</summary>
    [DeskAccess]
    [HttpDelete("{number:long}/cards/{card}")]
    public Task<ActionResult<object>> RemoveCard([FromRoute] long number, [FromRoute] string card, CancellationToken ct) =>
        Run<object>(async () =>
        {
            EnsureSpecialistEpic(number);
            await epics.RemoveCardAsync(number, card, await GetActorAsync(ct), ct);
            return NoContent();
        });

    /// <summary>
    /// Ajustes 2: acompanhamento das entregas — por card do épico: estado/coluna/responsável (DevOps), PRs do registro do PRMake
    /// (status gravado, sem consultar o GitHub) e ambientes do retrato da AWS (<c>null</c> sem retrato). Uma chamada, lotes internos.
    /// </summary>
    [HttpGet("{number:long}/monitor")]
    public Task<ActionResult<IReadOnlyList<EpicMonitorCard>>> Monitor([FromRoute] long number, [FromServices] EpicMonitorService monitor,
        CancellationToken ct) =>
        Run<IReadOnlyList<EpicMonitorCard>>(async () =>
        {
            EnsureSpecialistEpic(number);
            return Ok(await monitor.GetAsync(number, ct));
        });

    /// <summary>Épicos em que o card está (chips na Bancada).</summary>
    [HttpGet("by-card/{card}")]
    public Task<ActionResult<IReadOnlyList<EpicSummary>>> ByCard([FromRoute] string card, CancellationToken ct) =>
        Run<IReadOnlyList<EpicSummary>>(async () => Ok(await epics.ListByCardAsync(card, ct)));

    /// <summary>Com o token do caso: o épico precisa ser o do caso.</summary>
    private void EnsureSpecialistEpic(long number)
    {
        if (CaseClaims.SpecialistOf(User) is not null && !IsOwnEpic(number))
            throw new ExecutionForbiddenException("O token do caso só vale para o épico do caso.");
    }

    private bool IsOwnEpic(long number) => EpicKey.TryParse(CaseClaims.CardOf(User), out var own) && own == number;
}
