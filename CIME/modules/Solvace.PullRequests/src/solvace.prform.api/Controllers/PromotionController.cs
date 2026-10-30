using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.prform.Promotion;
using PrDomainException = solvace.prform.domain.Entities.DomainException;

namespace solvace.prform.Controllers;

/// <summary>
/// Promoção de PRs em lote para release/hotfix (0074): prévia por card, criação do lote, acompanhamento e reenvio.
/// O cherry-pick roda no executor do usuário (<see cref="PromotionExecutorController"/>); aqui só se pede e se acompanha.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class PromotionController(PromotionService promotion, PromotionParser parser, solvace.timeline.application.Contracts.IUserRepository users)
    : ExecutionControllerBase(users)
{
    /// <summary>Para cada card: repositório, branch principal, commits, perfil e destinos já resolvidos no remoto.</summary>
    [HttpPost("preview")]
    public Task<ActionResult<PromotionPreviewResponse>> Preview([FromBody] PromotionPreviewRequest request, CancellationToken ct) =>
        Guard<PromotionPreviewResponse>(async () => Ok(await promotion.PreviewAsync(request, UserId(), ct)));

    /// <summary>Branches do repositório (com busca por trecho) para o seletor de destinos.</summary>
    [HttpGet("branches")]
    public Task<ActionResult<List<string>>> Branches([FromQuery] string repository, [FromQuery] string? search, [FromQuery] int take = 30, [FromQuery] bool refresh = false, CancellationToken ct = default) =>
        Guard<List<string>>(async () => Ok(await promotion.SearchBranchesAsync(repository, search, take, refresh, ct)));

    /// <summary>Resolve destinos digitados numa linha (nome real no remoto, PR já aberto) sem refazer a prévia.</summary>
    [HttpPost("resolve")]
    public Task<ActionResult<List<PromotionPreviewTarget>>> Resolve([FromBody] PromotionResolveRequest request, CancellationToken ct) =>
        Guard<List<PromotionPreviewTarget>>(async () => Ok(await promotion.ResolveAsync(request, ct)));

    /// <summary>Cria o lote e dispara (itens de cherry-pick esperam o executor; PR direto sai na hora).</summary>
    [HttpPost]
    public Task<ActionResult<PromotionBatchResponse>> Create([FromBody] PromotionCreateRequest request, CancellationToken ct) =>
        Guard<PromotionBatchResponse>(async () =>
        {
            var actor = await GetActorAsync(ct);
            return Ok(await promotion.CreateAsync(request, UserId(), actor.Name, ct));
        });

    /// <summary>Interpreta a mensagem do Release Manager (cards por regex; regras e versão pela IA do usuário). Não dispara nada.</summary>
    [HttpPost("parse")]
    public Task<ActionResult<PromotionParseResponse>> Parse([FromBody] PromotionParseRequest request, CancellationToken ct) =>
        Guard<PromotionParseResponse>(async () => Ok(await parser.ParseAsync(request.Text, ct)));

    /// <summary>Lotes do usuário, do mais novo para o mais antigo.</summary>
    [HttpGet]
    public Task<ActionResult<List<PromotionBatchResponse>>> Mine([FromQuery] int take = 20, CancellationToken ct = default) =>
        Guard<List<PromotionBatchResponse>>(async () => Ok(await promotion.ListMineAsync(UserId(), take, ct)));

    [HttpGet("{id:guid}")]
    public Task<ActionResult<PromotionBatchResponse>> Get([FromRoute] Guid id, CancellationToken ct) =>
        Guard<PromotionBatchResponse>(async () => Ok(await promotion.GetAsync(id, ct)));

    /// <summary>Reenvia os itens que falharam, ficaram em conflito ou foram cancelados.</summary>
    [HttpPost("{id:guid}/retry")]
    public Task<ActionResult<PromotionBatchResponse>> Retry([FromRoute] Guid id, [FromBody] PromotionRetryRequest? request, CancellationToken ct) =>
        Guard<PromotionBatchResponse>(async () => Ok(await promotion.RetryAsync(id, request ?? new PromotionRetryRequest(), UserId(), ct)));

    /// <summary>Cancela o que ainda está na fila (o que já rodou continua).</summary>
    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult<PromotionBatchResponse>> Cancel([FromRoute] Guid id, CancellationToken ct) =>
        Guard<PromotionBatchResponse>(async () => Ok(await promotion.CancelAsync(id, UserId(), ct)));

    private Guid UserId() => RequireUserId();

    private async Task<ActionResult<T>> Guard<T>(Func<Task<ActionResult>> action)
    {
        try
        {
            return await Run<T>(action);
        }
        catch (PrDomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }
}

/// <summary>
/// Lado do executor (<c>prmake-agent</c>) da promoção (0074): pega o próximo item, manda heartbeat e diz como terminou.
/// A credencial do executor leva as claims do dono, então o PR sai com o GitHub dele.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class PromotionExecutorController(PromotionService promotion, solvace.timeline.application.Contracts.IUserRepository users)
    : ExecutionControllerBase(users)
{
    /// <summary>Próximo item do dono (204 = nada agora).</summary>
    [HttpPost("next")]
    public async Task<ActionResult<PromotionClaimResponse>> Next(CancellationToken ct)
    {
        try
        {
            var claim = await promotion.NextForExecutorAsync(RequireUserId(), RequireWorkerId(), ct);
            return claim is null ? NoContent() : Ok(claim);
        }
        catch (Exception e) when (Handle(e) is { } result)
        {
            return result;
        }
    }

    [HttpPost("items/{id:guid}/heartbeat")]
    public async Task<ActionResult<PromotionHeartbeatResponse>> Heartbeat([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await promotion.HeartbeatAsync(id, RequireUserId(), RequireWorkerId(), ct));
        }
        catch (Exception e) when (Handle(e) is { } result)
        {
            return result;
        }
    }

    [HttpPost("items/{id:guid}/report")]
    public async Task<ActionResult<PromotionItemResponse>> Report([FromRoute] Guid id, [FromBody] PromotionReportRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await promotion.ReportAsync(id, RequireUserId(), RequireWorkerId(), request, ct));
        }
        catch (Exception e) when (Handle(e) is { } result)
        {
            return result;
        }
    }

    private ActionResult? Handle(Exception e) => e switch
    {
        PrDomainException or solvace.executionplans.domain.Entities.DomainException => BadRequest(new { error = e.Message }),
        solvace.executionplans.application.Contracts.ExecutionForbiddenException => StatusCode(StatusCodes.Status403Forbidden, new { error = e.Message }),
        _ => null
    };
}
