using solvace.prform.Execution;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using solvace.azure.application.Contract;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;
using solvace.azure.domain.Requests;
using solvace.timeline.application.Contracts;
using solvace.timeline.domain.Requests;

namespace solvace.prform.Controllers;

/// <summary>0085: <c>POST Azure/card/{id}/actions/set-state</c>.</summary>
public class SetCardStateRequest
{
    public string State { get; set; } = string.Empty;
    /// <summary>Comentário na Discussion do card (opcional).</summary>
    public string? Reason { get; set; }
}

/// <summary>0078: horas a lançar no card.</summary>
public class LogHoursRequest
{
    public decimal Hours { get; set; }
}

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class AzureController : ControllerBase
{
    private readonly IAzureService _azureService;
    private readonly IDevOpsActionsService _actionsService;
    private readonly ILogger<AzureController> _logger;

    public AzureController(IAzureService azureService, IDevOpsActionsService actionsService, ILogger<AzureController> logger)
    {
        _azureService = azureService;
        _actionsService = actionsService;
        _logger = logger;
    }

    [HttpGet("card/{id}")]
    public async Task<IActionResult> GetCard(string id, CancellationToken cancellationToken)
    {
        var result = await _azureService.GetCardAsync(id, cancellationToken);
        if (result == null)
            return BadRequest(new { error = "Erro ao buscar o card" });
        return Ok(result);
    }

    [HttpGet("card/{id}/full")]
    public async Task<IActionResult> GetCardFull(string id, CancellationToken cancellationToken)
    {
        var result = await _azureService.GetCardFullAsync(id, cancellationToken);
        if (result == null)
            return BadRequest(new { error = "Erro ao buscar as informações do card" });
        return Ok(result);
    }

    /// <summary>
    /// Resumo (título, estado, coluna, responsável, link) de vários cards numa chamada (home, 0051). É enfeite da
    /// lista: sem integração configurada ou com o DevOps fora, responde vazio em vez de erro.
    /// </summary>
    [HttpPost("cards/summary")]
    public async Task<ActionResult<IReadOnlyList<AzureCardSummaryResponse>>> GetCardsSummary([FromBody] List<string>? cards, CancellationToken cancellationToken)
    {
        var ids = (cards ?? [])
            .Select(c => int.TryParse(c?.Trim(), out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();
        try
        {
            return Ok(await _azureService.GetCardsSummaryAsync(ids, cancellationToken));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Resumo dos cards no DevOps indisponível");
            return Ok(Array.Empty<AzureCardSummaryResponse>());
        }
    }

    [HttpPost("card/{id}/rootcause")]
    [Consumes("application/json", "text/plain", "text/html", "text/markdown")]
    public async Task<IActionResult> UpdateRootCause([FromRoute] string id,[FromBody] UpdateRootCauseRequest body, CancellationToken cancellationToken)
    {

        var result = await _azureService.UpdateRootCauseAsync(id, body, cancellationToken);
        if (result == null)
            return BadRequest(new { error = "Erro ao atualizar a causa raiz" });
        return Ok(result);
    }

    // ---- Ações DevOps (feature 0011) ----

    /// <summary>Valores efetivos do usuário para montar o menu "Ações DevOps" (plugin AI Configurations).</summary>
    [HttpGet("actions/config")]
    public async Task<ActionResult<DevOpsActionsConfigResponse>> GetActionsConfig(CancellationToken cancellationToken) =>
        Ok(await _actionsService.GetConfigAsync(cancellationToken));

    /// <summary>Move o card (Bug, sem pendências, na área exigida) para Test in production, com comentário.</summary>
    [HttpPost("card/{id}/actions/test-in-production")]
    public Task<ActionResult<DevOpsActionResponse>> MoveToTestInProduction(string id, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.MoveToTestInProductionAsync(id, cancellationToken), cancellationToken, "test-in-production");

    /// <summary>Move o card (Bug, sem pendências) para o estado de Ready for QA.</summary>
    [HttpPost("card/{id}/actions/ready-for-qa")]
    public Task<ActionResult<DevOpsActionResponse>> MoveToReadyForQa(string id, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.MoveToReadyForQaAsync(id, cancellationToken), cancellationToken, "ready-for-qa");

    /// <summary>Move o card (Bug) para Dev Test in QA: o dev validando a correção em QA (estado + coluna do board).</summary>
    [HttpPost("card/{id}/actions/dev-test-in-qa")]
    public Task<ActionResult<DevOpsActionResponse>> MoveToDevTestInQa(string id, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.MoveToDevTestInQaAsync(id, cancellationToken), cancellationToken, "dev-test-in-qa");

    /// <summary>Preenche Original Estimate / Remaining Work / Completed Work com os valores do usuário.</summary>
    [HttpPost("card/{id}/actions/initial-estimate")]
    public Task<ActionResult<DevOpsActionResponse>> SetInitialEstimate(string id, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.SetInitialEstimateAsync(id, cancellationToken), cancellationToken, "initial-estimate");

    /// <summary>Opções de classificação (padrão ou as do "AI Configurations" do usuário) — feature 0028.</summary>
    [HttpGet("actions/classifications")]
    public async Task<ActionResult<IReadOnlyList<DevOpsClassificationPreset>>> GetClassifications(CancellationToken cancellationToken) =>
        Ok(await _actionsService.GetClassificationPresetsAsync(cancellationToken));

    /// <summary>Valores do picklist Bug Type do Bug, lidos do DevOps (o que o classify aceita em bugType).</summary>
    [HttpGet("actions/bug-types")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetBugTypes(CancellationToken cancellationToken) =>
        Ok(await _actionsService.GetBugTypesAsync(cancellationToken));

    /// <summary>
    /// Classifica o card (Resolution Type · General Classification · Classification) pela integração do Azure do
    /// usuário e registra na Timeline — body { preset } ou os três valores, e/ou { bugType } (feature 0028).
    /// </summary>
    [HttpPost("card/{id}/actions/classify")]
    public Task<ActionResult<DevOpsActionResponse>> Classify(string id, [FromBody] ClassifyCardRequest request, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.ClassifyAsync(id, request, cancellationToken), cancellationToken, "classify");

    /// <summary>0078: horas do card (Original Estimate, Completed Work, Remaining Work).</summary>
    [HttpGet("card/{id}/effort")]
    public async Task<ActionResult<DevOpsEffortResponse>> GetEffort(string id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _actionsService.GetEffortAsync(id, cancellationToken));
        }
        catch (DevOpsActionException e)
        {
            return StatusCode(e.StatusCode, new { error = e.Message });
        }
        catch (Exception e) when (e is InvalidOperationException or solvace.prform.application.UserIntegrations.PersonalIntegrationRequiredException)
        {
            // Sem integração do DevOps configurada: a tela mostra "Horas indisponíveis".
            return Conflict(new { error = e.Message });
        }
    }

    /// <summary>
    /// 0085 r3: estados válidos do tipo do card no DevOps (lista do editor "Mover o card") —
    /// <c>{ type, current, states:[{name, category?, suggested, action?, current}] }</c>. Card inexistente → 404, DevOps fora → 502.
    /// </summary>
    [HttpGet("card/{id}/states")]
    public async Task<ActionResult<CardStatesResponse>> GetCardStates(string id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _actionsService.GetCardStatesAsync(id, cancellationToken));
        }
        catch (DevOpsActionException e)
        {
            return StatusCode(e.StatusCode, new { error = e.Message });
        }
        catch (InvalidOperationException e)
        {
            // Integração do DevOps incompleta (organização/projeto). Sem integração pessoal → 403 (filtro global).
            return Conflict(new { error = e.Message });
        }
    }

    /// <summary>0078: lança horas — soma em Completed Work e tira do Remaining Work.</summary>
    [HttpPost("card/{id}/actions/log-hours")]
    public Task<ActionResult<DevOpsActionResponse>> LogHours(string id, [FromBody] LogHoursRequest request, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.LogHoursAsync(id, request.Hours, cancellationToken), cancellationToken, "log-hours", hours: request.Hours);

    /// <summary>
    /// 0085: muda o estado do card para um estado válido do tipo dele (Bug/User Story) — a skill desfazer-item-de-trabalho
    /// usa para voltar o estado anterior de uma sub-tarefa devops-state. Registra na Timeline.
    /// </summary>
    [HttpPost("card/{id}/actions/set-state")]
    public Task<ActionResult<DevOpsActionResponse>> SetState(string id, [FromBody] SetCardStateRequest request, [FromServices] ITimelineApplication timeline,
        CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.SetStateAsync(id, request.State, request.Reason, cancellationToken), cancellationToken, "set-state", state: request.State);

    /// <summary>Zera o Remaining Work do card.</summary>
    [HttpPost("card/{id}/actions/zero-remaining")]
    public Task<ActionResult<DevOpsActionResponse>> ZeroRemaining(string id, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.ZeroRemainingAsync(id, cancellationToken), cancellationToken, "zero-remaining");

    /// <summary>
    /// Comentário novo na discussion do card (sub-tarefa <c>comment</c> da Bancada), sem exigir o registro do card no
    /// PRMake; imagens opcionais (base64) sobem como anexos do DevOps e entram no fim do texto. Registra na Timeline e
    /// vira entrega da sub-tarefa (<c>?workItemTask=</c>).
    /// </summary>
    [HttpPost("card/{id}/comment")]
    [RequestSizeLimit(80_000_000)] // até 10 imagens de 5 MB em base64
    public async Task<ActionResult<DevOpsCommentResponse>> PostComment(string id, [FromBody] PostCommentRequest request,
        [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken)
    {
        DevOpsCommentResponse result;
        try
        {
            result = await _actionsService.PostCommentAsync(id, request, cancellationToken);
        }
        catch (DevOpsActionException e)
        {
            return StatusCode(e.StatusCode, new { error = e.Message });
        }

        await RegisterOnTimelineAsync(timeline, id, $"{result.Message} pelo CIME.", _logger, User, cancellationToken);
        if (HttpContext.RequestServices.GetService<WorkDeliverableHooks>() is { } hooks)
            await hooks.CommentAsync(id, result.Message, result.CommentId, WorkDeliverableHooks.TaskOf(HttpContext),
                WorkDeliverableHooks.IsClaude(HttpContext), WorkDeliverableHooks.ActorOf(HttpContext), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Executa a ação e registra na Timeline do card. Regra não atendida = 409, card inexistente =
    /// 404, erro do DevOps = 502 — sempre com { error } legível para o usuário.
    /// </summary>
    private async Task<ActionResult<DevOpsActionResponse>> RunActionAsync(string cardNumber, ITimelineApplication timeline,
        Func<Task<DevOpsActionResponse>> action, CancellationToken cancellationToken, string actionKey, decimal? hours = null, string? state = null)
    {
        DevOpsActionResponse result;
        try
        {
            result = await action();
        }
        catch (DevOpsActionException e)
        {
            return StatusCode(e.StatusCode, new { error = e.Message });
        }

        await RegisterOnTimelineAsync(timeline, cardNumber, $"{result.Message} pelo CIME (Ações DevOps).", _logger, User, cancellationToken);
        // 0091: a ação vira entrega da sub-tarefa (?workItemTask= da skill, ou a de DevOps em andamento no card).
        if (HttpContext.RequestServices.GetService<WorkDeliverableHooks>() is { } hooks)
            await hooks.DevOpsAsync(cardNumber, actionKey, result.Message, hours, state, WorkDeliverableHooks.TaskOf(HttpContext),
                WorkDeliverableHooks.IsClaude(HttpContext), WorkDeliverableHooks.ActorOf(HttpContext), cancellationToken);
        return Ok(result);
    }

    /// <summary>Linha do tempo do card: usuário logado (claim ExternalId). Falha só é logada.</summary>
    internal static async Task RegisterOnTimelineAsync(ITimelineApplication timeline, string cardNumber, string description,
        ILogger logger, System.Security.Claims.ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var claim = user.FindFirst("ExternalId")?.Value;
        Guid? userId = !string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out var parsed) ? parsed : null;

        try
        {
            await timeline.CreateAsync(new CreateTimelineEntryRequest
            {
                CardNumber = cardNumber,
                Description = description,
                UserName = userId is null ? "CIME" : null
            }, userId, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Ação no DevOps concluída, mas não foi possível registrar na timeline do card {CardNumber}", cardNumber);
        }
    }
}
