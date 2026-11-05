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
        RunActionAsync(id, timeline, () => _actionsService.MoveToTestInProductionAsync(id, cancellationToken), cancellationToken);

    /// <summary>Move o card (Bug, sem pendências) para o estado de Ready for QA.</summary>
    [HttpPost("card/{id}/actions/ready-for-qa")]
    public Task<ActionResult<DevOpsActionResponse>> MoveToReadyForQa(string id, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.MoveToReadyForQaAsync(id, cancellationToken), cancellationToken);

    /// <summary>Move o card (Bug) para Dev Test in QA: o dev validando a correção em QA (estado + coluna do board).</summary>
    [HttpPost("card/{id}/actions/dev-test-in-qa")]
    public Task<ActionResult<DevOpsActionResponse>> MoveToDevTestInQa(string id, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.MoveToDevTestInQaAsync(id, cancellationToken), cancellationToken);

    /// <summary>Preenche Original Estimate / Remaining Work / Completed Work com os valores do usuário.</summary>
    [HttpPost("card/{id}/actions/initial-estimate")]
    public Task<ActionResult<DevOpsActionResponse>> SetInitialEstimate(string id, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.SetInitialEstimateAsync(id, cancellationToken), cancellationToken);

    /// <summary>Opções de classificação (padrão ou as do "AI Configurations" do usuário) — feature 0028.</summary>
    [HttpGet("actions/classifications")]
    public async Task<ActionResult<IReadOnlyList<DevOpsClassificationPreset>>> GetClassifications(CancellationToken cancellationToken) =>
        Ok(await _actionsService.GetClassificationPresetsAsync(cancellationToken));

    /// <summary>
    /// Classifica o card (Resolution Type · General Classification · Classification) pela integração do Azure do
    /// usuário e registra na Timeline — body { preset } ou os três valores (feature 0028).
    /// </summary>
    [HttpPost("card/{id}/actions/classify")]
    public Task<ActionResult<DevOpsActionResponse>> Classify(string id, [FromBody] ClassifyCardRequest request, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.ClassifyAsync(id, request, cancellationToken), cancellationToken);

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

    /// <summary>0078: lança horas — soma em Completed Work e tira do Remaining Work.</summary>
    [HttpPost("card/{id}/actions/log-hours")]
    public Task<ActionResult<DevOpsActionResponse>> LogHours(string id, [FromBody] LogHoursRequest request, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.LogHoursAsync(id, request.Hours, cancellationToken), cancellationToken);

    /// <summary>
    /// 0085: muda o estado do card para um estado válido do tipo dele (Bug/User Story) — a skill desfazer-item-de-trabalho
    /// usa para voltar o estado anterior de uma sub-tarefa devops-state. Registra na Timeline.
    /// </summary>
    [HttpPost("card/{id}/actions/set-state")]
    public Task<ActionResult<DevOpsActionResponse>> SetState(string id, [FromBody] SetCardStateRequest request, [FromServices] ITimelineApplication timeline,
        CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.SetStateAsync(id, request.State, request.Reason, cancellationToken), cancellationToken);

    /// <summary>Zera o Remaining Work do card.</summary>
    [HttpPost("card/{id}/actions/zero-remaining")]
    public Task<ActionResult<DevOpsActionResponse>> ZeroRemaining(string id, [FromServices] ITimelineApplication timeline, CancellationToken cancellationToken) =>
        RunActionAsync(id, timeline, () => _actionsService.ZeroRemainingAsync(id, cancellationToken), cancellationToken);

    /// <summary>
    /// Executa a ação e registra na Timeline do card. Regra não atendida = 409, card inexistente =
    /// 404, erro do DevOps = 502 — sempre com { error } legível para o usuário.
    /// </summary>
    private async Task<ActionResult<DevOpsActionResponse>> RunActionAsync(string cardNumber, ITimelineApplication timeline,
        Func<Task<DevOpsActionResponse>> action, CancellationToken cancellationToken)
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
