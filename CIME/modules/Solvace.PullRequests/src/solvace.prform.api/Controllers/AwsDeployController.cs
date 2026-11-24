using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.prform.AwsDeploy;
using ExecDomainException = solvace.executionplans.domain.Entities.DomainException;

namespace solvace.prform.Controllers;

/// <summary>
/// 0100: aba AWS da Bancada — configuração, índice de pipelines (compartilhado), contexto do card para o script, retrato
/// por card e o pedido de coleta (Atualizar). Para todo usuário autenticado (não é só admin): a coleta roda no executor de
/// quem pediu. O script grava com a api-key da pessoa ou com a credencial do executor dela.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class AwsDeployController(AwsDeployService aws, solvace.timeline.application.Contracts.IUserRepository users)
    : ExecutionControllerBase(users)
{
    /// <summary>Ambientes (AwsDeployEnvironments) e o acompanhamento do deploy em andamento.</summary>
    [HttpGet("config")]
    public Task<ActionResult<AwsDeployConfigResponse>> Config(CancellationToken ct) =>
        Guard<AwsDeployConfigResponse>(async () => Ok(await aws.GetConfigAsync(ct)));

    /// <summary>Índice: só nome + <c>updated</c> da AWS (o script decide o que buscar).</summary>
    [HttpGet("pipelines/index")]
    public Task<ActionResult<AwsPipelineIndexResponse>> Index(CancellationToken ct) =>
        Guard<AwsPipelineIndexResponse>(async () => Ok(await aws.GetIndexAsync(ct)));

    /// <summary>Novas/alteradas (até 1500), removidas e, com <c>full</c>, remove o que não veio.</summary>
    [HttpPost("pipelines/index")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public Task<ActionResult<AwsPipelineIndexResult>> UpsertIndex([FromBody] AwsPipelineIndexRequest request, CancellationToken ct) =>
        Guard<AwsPipelineIndexResult>(async () => Ok(await aws.UpsertIndexAsync(request, ct)));

    /// <summary>Pipelines do índice cuja fonte é o repositório (ex.: electradv/edv-solvace-apps).</summary>
    [HttpGet("pipelines")]
    public Task<ActionResult<List<AwsPipelineDto>>> Pipelines([FromQuery] string repository, CancellationToken ct) =>
        Guard<List<AwsPipelineDto>>(async () => Ok(await aws.ListByRepositoryAsync(repository, ct)));

    /// <summary>Repositórios, branches, SHAs e PRs do card + marcadores e ambientes (entrada do script de coleta).</summary>
    [HttpGet("card/{card}/context")]
    public Task<ActionResult<AwsCardContextResponse>> Context([FromRoute] string card, CancellationToken ct) =>
        Guard<AwsCardContextResponse>(async () => Ok(await aws.GetContextAsync(card, ct)));

    /// <summary>Grava o retrato (≤ 2 MB), conclui o pedido do <c>requestId</c> e agenda o acompanhamento se há deploy em andamento.</summary>
    [HttpPut("card/{card}")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public Task<ActionResult<AwsCardResponse>> PutSnapshot([FromRoute] string card, [FromBody] JsonElement snapshot, CancellationToken ct) =>
        Guard<AwsCardResponse>(async () =>
        {
            var actor = await GetActorAsync(ct);
            return Ok(await aws.PutSnapshotAsync(card, snapshot, actor.UserId, actor.Name, ct));
        });

    /// <summary>Retrato, quem/quando coletou, o pedido de coleta atual e se quem olha tem executor ativo.</summary>
    [HttpGet("card/{card}")]
    public Task<ActionResult<AwsCardResponse>> Get([FromRoute] string card, CancellationToken ct) =>
        Guard<AwsCardResponse>(async () => Ok(await aws.GetCardAsync(card, CurrentUserId, ct)));

    /// <summary>Pede a coleta ao executor de quem clicou (409 <c>NO_EXECUTOR</c> sem executor ativo).</summary>
    [HttpPost("card/{card}/refresh")]
    public Task<ActionResult<AwsRefreshResponse>> Refresh([FromRoute] string card, CancellationToken ct) =>
        Guard<AwsRefreshResponse>(async () =>
        {
            var actor = await GetActorAsync(ct);
            return Ok(await aws.RequestRefreshAsync(card, RequireUserId(), actor.Name, ct));
        });

    /// <summary>Erros como os da fila (400/403/404/409); sem executor ativo → 409 com <c>code: NO_EXECUTOR</c>.</summary>
    private Task<ActionResult<T>> Guard<T>(Func<Task<ActionResult>> action) => Run<T>(async () =>
    {
        try
        {
            return await action();
        }
        catch (AwsNoExecutorException e)
        {
            return Conflict(new { error = e.Message, code = AwsNoExecutorException.Code });
        }
    });
}

/// <summary>
/// 0100: lado do executor (<c>prmake-agent</c>, laço AWS) da coleta: pega o próximo pedido DO DONO da credencial (regra
/// 78723 — nunca o de outra pessoa), estende a trava e diz como terminou. Qualquer pessoa autenticada (não é só admin).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class AwsDeployExecutorController(AwsDeployService aws, solvace.timeline.application.Contracts.IUserRepository users)
    : ExecutionControllerBase(users)
{
    /// <summary>Próximo pedido do dono (204 = nada agora). Resposta: <c>{ id, card, requestedBy, attempt, source }</c>.</summary>
    [HttpPost("next")]
    public async Task<ActionResult<AwsRefreshClaimResponse>> Next(CancellationToken ct)
    {
        try
        {
            var claim = await aws.NextForExecutorAsync(RequireUserId(), RequireWorkerId(), ct);
            return claim is null ? NoContent() : Ok(claim);
        }
        catch (Exception e) when (Handle(e) is { } result)
        {
            return result;
        }
    }

    /// <summary>Coleta longa (índice inteiro): estende a trava de 5 min. <c>{ stop: true }</c> = o pedido não é mais deste executor.</summary>
    [HttpPost("{id:guid}/heartbeat")]
    public async Task<ActionResult> Heartbeat([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await aws.HeartbeatAsync(id, RequireUserId(), RequireWorkerId(), ct);
            return Ok(new { stop = !ok });
        }
        catch (Exception e) when (Handle(e) is { } result)
        {
            return result;
        }
    }

    /// <summary>Fim: <c>{ status: done|failed, error?, durationMs }</c>.</summary>
    [HttpPost("{id:guid}/report")]
    public async Task<ActionResult<AwsRefreshResponse>> Report([FromRoute] Guid id, [FromBody] AwsRefreshReportRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await aws.ReportAsync(id, RequireUserId(), RequireWorkerId(), request, ct));
        }
        catch (Exception e) when (Handle(e) is { } result)
        {
            return result;
        }
    }

    private ActionResult? Handle(Exception e) => e switch
    {
        ExecDomainException or solvace.prform.domain.Entities.DomainException => BadRequest(new { error = e.Message }),
        solvace.executionplans.application.Contracts.ExecutionForbiddenException => StatusCode(StatusCodes.Status403Forbidden, new { error = e.Message }),
        _ => null
    };
}
