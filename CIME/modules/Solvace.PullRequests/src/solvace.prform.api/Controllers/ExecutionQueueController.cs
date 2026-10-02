using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;

namespace solvace.prform.Controllers;

/// <summary>Base dos controllers da fila (0039): quem está chamando e o mapeamento de erros.</summary>
public abstract class ExecutionControllerBase(solvace.timeline.application.Contracts.IUserRepository users) : ControllerBase
{
    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst("ExternalId")?.Value, out var id) ? id : null;

    protected async Task<ExecutionActor> GetActorAsync(CancellationToken ct)
    {
        var userId = CurrentUserId;
        string? name = null;
        if (userId.HasValue)
            name = await users.GetFullNameAsync(userId.Value, ct);
        name ??= User.FindFirst(ClaimTypes.Name)?.Value;
        return new ExecutionActor(userId, string.IsNullOrWhiteSpace(name) ? "Usuário" : name.Trim(), false);
    }

    /// <summary>Id do executor da credencial (endpoints que só o executor chama).</summary>
    protected Guid RequireWorkerId() =>
        ExecutorClaims.WorkerIdOf(User) ?? throw new ExecutionForbiddenException("Este endpoint é do executor (prmake-agent) — use a credencial dele.");

    protected Guid RequireUserId() =>
        CurrentUserId ?? throw new DomainException("Seu usuário não tem identificação no PRMake — gere a api-key de novo.");

    protected async Task<ActionResult<T>> Run<T>(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception e) when (Map(e) is { } result)
        {
            return result;
        }
    }

    protected async Task<ActionResult> RunPlain(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception e) when (Map(e) is { } result)
        {
            return result;
        }
    }

    private ActionResult? Map(Exception e) => e switch
    {
        DomainException => BadRequest(new { error = e.Message }),
        ExecutionForbiddenException => StatusCode(StatusCodes.Status403Forbidden, new { error = e.Message }),
        ExecutionPlanNotFoundException => NotFound(new { error = e.Message }),
        ExecutionPlanConcurrencyException => Conflict(new { error = "O pedido foi alterado ao mesmo tempo por outra ação. Tente de novo." }),
        _ => null
    };
}

/// <summary>
/// Fila de execução (0039): a tela pede "Analisar/Continuar com Claude"; o executor (<c>prmake-agent</c>) pega o
/// pedido por long-poll, avisa que começou, manda heartbeat e diz como terminou.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ExecutionQueueController(IExecutionQueueApplication queue, solvace.timeline.application.Contracts.IUserRepository users)
    : ExecutionControllerBase(users)
{
    /// <summary>Pede para rodar a skill no card (na máquina do usuário).</summary>
    [HttpPost]
    public Task<ActionResult<ExecutionRequestResponse>> Create([FromBody] CreateExecutionRequestRequest request, CancellationToken ct) =>
        Run<ExecutionRequestResponse>(async () => Ok(await queue.CreateAsync(request, await GetActorAsync(ct), ExecutionRequestSource.Button, ct)));

    /// <summary>Pedido ativo do card, últimos pedidos e as máquinas de quem está vendo.</summary>
    [HttpGet("card/{cardNumber}")]
    public Task<ActionResult<ExecutionCardQueueResponse>> GetCard([FromRoute] string cardNumber, CancellationToken ct) =>
        Run<ExecutionCardQueueResponse>(async () => Ok(await queue.GetCardAsync(cardNumber, CurrentUserId, ct)));

    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult<ExecutionRequestResponse>> Cancel([FromRoute] Guid id, [FromBody] CancelExecutionRequestRequest? request, CancellationToken ct) =>
        Run<ExecutionRequestResponse>(async () => Ok(await queue.CancelAsync(id, request?.Reason, await GetActorAsync(ct), ct)));

    /// <summary>Pede de novo um pedido que falhou/expirou/foi cancelado.</summary>
    [HttpPost("{id:guid}/retry")]
    public Task<ActionResult<ExecutionRequestResponse>> Retry([FromRoute] Guid id, CancellationToken ct) =>
        Run<ExecutionRequestResponse>(async () => Ok(await queue.RetryAsync(id, await GetActorAsync(ct), ct)));

    /// <summary>"Rodar mesmo assim": libera o pedido com o orçamento do dia estourado.</summary>
    [HttpPost("{id:guid}/force")]
    public Task<ActionResult<ExecutionRequestResponse>> Force([FromRoute] Guid id, CancellationToken ct) =>
        Run<ExecutionRequestResponse>(async () => Ok(await queue.AllowOverBudgetAsync(id, await GetActorAsync(ct), ct)));

    /// <summary>Pedidos do usuário (histórico das máquinas dele).</summary>
    [HttpGet("mine")]
    public Task<ActionResult<List<ExecutionRequestResponse>>> Mine(CancellationToken ct) =>
        Run<List<ExecutionRequestResponse>>(async () => Ok(await queue.GetHistoryAsync(RequireUserId(), ct)));

    // ── Executor ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Long-poll do executor: 200 com o pedido assim que existir, 204 depois de <paramref name="wait"/> s (máx. 25).</summary>
    [HttpGet("next")]
    public Task<ActionResult<ExecutionClaimResponse>> Next([FromQuery] int wait,
        [FromServices] solvace.prform.application.UserIntegrations.IPluginConfigurationResolver settings,
        [FromServices] ILogger<ExecutionQueueController> logger, CancellationToken ct) =>
        Run<ExecutionClaimResponse>(async () =>
        {
            var claim = await queue.NextAsync(RequireWorkerId(), TimeSpan.FromSeconds(Math.Clamp(wait, 0, 25)), ct);
            if (claim is null) return NoContent();
            claim.Model = await ModelForAsync(claim.Phase, settings, logger, ct);
            return Ok(claim);
        });

    /// <summary>
    /// 0047: modelo da fase pela "Skills Configurations" (ExecutorAnalysisModel / ExecutorCorrectionModel). Sem a
    /// configuração (ou falha ao ler), null — o executor usa o modelo padrão da máquina.
    /// </summary>
    private static async Task<string?> ModelForAsync(string phase,
        solvace.prform.application.UserIntegrations.IPluginConfigurationResolver settings, ILogger logger, CancellationToken ct)
    {
        try
        {
            var config = await settings.GetEffectiveConfigurationAsync(solvace.prform.Skills.SkillsConfigurationKeys.PluginName, ct);
            var key = phase == ExecutionPhase.Correction
                ? solvace.prform.Skills.SkillsConfigurationKeys.ExecutorCorrectionModel
                : solvace.prform.Skills.SkillsConfigurationKeys.ExecutorAnalysisModel;
            var model = solvace.prform.domain.Extensions.PluginConfigurationExtensions.GetConfigurationValueOrDefault(config, key, string.Empty).Trim();
            // Só apelido ou id de modelo (vai como argumento do Claude Code).
            return model.Length is > 0 and <= 100 && model.All(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '_' or '[' or ']') ? model : null;
        }
        catch (Exception e) when (e is InvalidOperationException or solvace.prform.application.UserIntegrations.PersonalIntegrationRequiredException)
        {
            logger.LogWarning(e, "Sem a configuração do modelo do executor (fase {Phase}) — vai o padrão da máquina", phase);
            return null;
        }
    }

    [HttpPost("{id:guid}/start")]
    public Task<ActionResult<ExecutionRequestResponse>> Start([FromRoute] Guid id, [FromBody] StartExecutionRequestRequest request, CancellationToken ct) =>
        Run<ExecutionRequestResponse>(async () => Ok(await queue.StartAsync(id, RequireWorkerId(), request, ct)));

    [HttpPost("{id:guid}/heartbeat")]
    public Task<ActionResult<ExecutionHeartbeatResponse>> Heartbeat([FromRoute] Guid id, [FromBody] ExecutionRequestHeartbeatRequest request, CancellationToken ct) =>
        Run<ExecutionHeartbeatResponse>(async () => Ok(await queue.HeartbeatAsync(id, RequireWorkerId(), request, ct)));

    [HttpPost("{id:guid}/finish")]
    public Task<ActionResult<ExecutionRequestResponse>> Finish([FromRoute] Guid id, [FromBody] FinishExecutionRequestRequest request,
        [FromServices] IExecutionPlanApplication plans, [FromServices] ILogger<ExecutionQueueController> logger, CancellationToken ct) =>
        Run<ExecutionRequestResponse>(async () =>
        {
            var response = await queue.FinishAsync(id, RequireWorkerId(), request, ct);
            // 0044: consumo FINAL da sessão (lido do transcript pelo executor) — a skill só manda fotos no meio da execução.
            if (request.SessionUsage is { } usage && !string.IsNullOrWhiteSpace(usage.SessionId) && usage.Turns > 0)
            {
                try
                {
                    if (await queue.ResolvePlanIdAsync(id, usage.SessionId, ct) is { } planId)
                        await plans.RecordUsageAsync(planId, usage, ct, sessionEnded: true);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "Não gravei o consumo final da sessão do pedido {RequestId}", id);
                }
            }
            return Ok(response);
        });
}

/// <summary>
/// Executores (0039): registro da máquina (com a api-key do usuário → credencial própria), sinal de vida, doctor,
/// "Meus executores" (pausar, retomar, revogar, concorrência), configurações do usuário e download do executor.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ExecutionWorkerController(IExecutionQueueApplication queue, ExecutorTokenIssuer issuer, ExecutionAgentCatalog agents,
    solvace.timeline.application.Contracts.IUserRepository users) : ExecutionControllerBase(users)
{
    /// <summary>Registra (ou registra de novo) esta máquina e devolve a credencial do executor — só aparece aqui.</summary>
    [HttpPost("register")]
    public Task<ActionResult<ExecutionWorkerRegistrationResponse>> Register([FromBody] RegisterExecutionWorkerRequest request, CancellationToken ct) =>
        Run<ExecutionWorkerRegistrationResponse>(async () =>
        {
            if (ExecutorClaims.WorkerIdOf(User) is not null)
                throw new ExecutionForbiddenException("Registre a máquina com a api-key do usuário, não com a credencial de um executor.");
            var (worker, response) = await queue.RegisterWorkerAsync(request, await GetActorAsync(ct), ct);
            return Ok(new ExecutionWorkerRegistrationResponse { Worker = response, Token = issuer.Issue(User, worker) });
        });

    /// <summary>Máquinas do usuário.</summary>
    [HttpGet("mine")]
    public Task<ActionResult<List<ExecutionWorkerResponse>>> Mine(CancellationToken ct) =>
        Run<List<ExecutionWorkerResponse>>(async () => Ok(await queue.GetWorkersAsync(RequireUserId(), ct)));

    /// <summary>O executor pergunta quem ele é (status, concorrência, versão publicada).</summary>
    [HttpGet("me")]
    public Task<ActionResult<ExecutionWorkerResponse>> Me(CancellationToken ct) =>
        Run<ExecutionWorkerResponse>(async () => Ok(await queue.GetWorkerAsync(RequireWorkerId(), ct)));

    /// <summary>Sinal de vida do executor com versões e capacidades; devolve o estado e os pedidos que o PRMake acha que estão com ele.</summary>
    [HttpPost("report")]
    public Task<ActionResult<ExecutionWorkerStateResponse>> Report([FromBody] ExecutionWorkerReportRequest request, CancellationToken ct) =>
        Run<ExecutionWorkerStateResponse>(async () => Ok(await queue.ReportWorkerAsync(RequireWorkerId(), request, ct)));

    [HttpPost("doctor")]
    public Task<ActionResult<ExecutionWorkerResponse>> Doctor([FromBody] ExecutionWorkerDoctorRequest request, CancellationToken ct) =>
        Run<ExecutionWorkerResponse>(async () => Ok(await queue.ReportDoctorAsync(RequireWorkerId(), request, ct)));

    [HttpPatch("{id:guid}")]
    public Task<ActionResult<ExecutionWorkerResponse>> Configure([FromRoute] Guid id, [FromBody] ConfigureExecutionWorkerRequest request, CancellationToken ct) =>
        Run<ExecutionWorkerResponse>(async () => Ok(await queue.ConfigureWorkerAsync(id, request, await GetActorAsync(ct), ct)));

    /// <summary>"Rodar diagnóstico agora": o executor roda o doctor no próximo sinal de vida (até 1 min).</summary>
    [HttpPost("{id:guid}/doctor-request")]
    public Task<ActionResult<ExecutionWorkerResponse>> RequestDoctor([FromRoute] Guid id, CancellationToken ct) =>
        Run<ExecutionWorkerResponse>(async () => Ok(await queue.RequestDoctorAsync(id, await GetActorAsync(ct), ct)));

    [HttpPost("{id:guid}/pause")]
    public Task<ActionResult<ExecutionWorkerResponse>> Pause([FromRoute] Guid id, CancellationToken ct) =>
        Run<ExecutionWorkerResponse>(async () => Ok(await queue.PauseWorkerAsync(id, await GetActorAsync(ct), ct)));

    [HttpPost("{id:guid}/resume")]
    public Task<ActionResult<ExecutionWorkerResponse>> Resume([FromRoute] Guid id, CancellationToken ct) =>
        Run<ExecutionWorkerResponse>(async () => Ok(await queue.ResumeWorkerAsync(id, await GetActorAsync(ct), ct)));

    [HttpPost("{id:guid}/revoke")]
    public Task<ActionResult<ExecutionWorkerResponse>> Revoke([FromRoute] Guid id, CancellationToken ct) =>
        Run<ExecutionWorkerResponse>(async () => Ok(await queue.RevokeWorkerAsync(id, await GetActorAsync(ct), ct)));

    /// <summary>Orçamento diário e regra automática do usuário.</summary>
    [HttpGet("settings")]
    public Task<ActionResult<ExecutionUserSettingsResponse>> GetSettings(CancellationToken ct) =>
        Run<ExecutionUserSettingsResponse>(async () => Ok(await queue.GetSettingsAsync(RequireUserId(), ct)));

    [HttpPut("settings")]
    public Task<ActionResult<ExecutionUserSettingsResponse>> UpdateSettings([FromBody] UpdateExecutionUserSettingsRequest request, CancellationToken ct) =>
        Run<ExecutionUserSettingsResponse>(async () => Ok(await queue.UpdateSettingsAsync(RequireUserId(), request, ct)));

    /// <summary>Versão publicada do executor e os sistemas disponíveis.</summary>
    [HttpGet("agent")]
    public ActionResult<object> Agent() => Ok(new { version = agents.LatestVersion, rids = agents.Available() });

    /// <summary>Binário do executor para o sistema (osx-arm64, osx-x64, win-x64, linux-x64, linux-arm64).</summary>
    [HttpGet("agent/{rid}")]
    public ActionResult AgentBinary([FromRoute] string rid)
    {
        var path = agents.BinaryPath(rid);
        if (path is null)
            return NotFound(new { error = $"Executor para '{rid}' não publicado nesta versão do PRMake." });
        Response.Headers["X-Agent-Version"] = agents.LatestVersion ?? string.Empty;
        return PhysicalFile(path, "application/octet-stream", Path.GetFileName(path));
    }
}
