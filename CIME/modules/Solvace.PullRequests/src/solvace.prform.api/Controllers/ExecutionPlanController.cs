using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.prform.Controllers;

/// <summary>
/// Plano de execução das skills (feature 0023): a skill (Claude Code, com a api-key do usuário) cria o
/// plano, manda o andamento em pedaços e os arquivos; a tela do card acompanha em tempo real e pode
/// pausar, continuar ou cancelar.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ExecutionPlanController : ControllerBase
{
    /// <summary>Header que a skill manda para se identificar como executora (heartbeat, "retomar").</summary>
    public const string ExecutorHeader = "X-Execution-Client";

    private const long MaxUploadRequestBytes = ExecutionArtifact.MaxFileBytes + 1024 * 1024;
    /// <summary>Comentário com vários anexos (0031): até o limite do plano.</summary>
    private const long MaxNoteRequestBytes = ExecutionArtifact.MaxPlanBytes + 1024 * 1024;

    private static readonly FileExtensionContentTypeProvider ContentTypes = CreateContentTypes();

    private readonly IExecutionPlanApplication _application;
    private readonly solvace.timeline.application.Contracts.IUserRepository _users;

    public ExecutionPlanController(
        IExecutionPlanApplication application,
        solvace.timeline.application.Contracts.IUserRepository users)
    {
        _application = application;
        _users = users;
    }

    /// <summary>Cria o plano de um card (a skill chama no início da análise).</summary>
    [HttpPost]
    public Task<ActionResult<ExecutionPlanResponse>> Create([FromBody] CreateExecutionPlanRequest request, CancellationToken ct) =>
        Run<ExecutionPlanResponse>(async () => Ok(await _application.CreateAsync(request, await GetActorAsync(ct, executor: true), ct)));

    /// <summary>Planos do card, do mais recente para o mais antigo (histórico).</summary>
    [HttpGet("card/{cardNumber}")]
    public Task<ActionResult<List<ExecutionPlanSummaryResponse>>> GetByCard([FromRoute] string cardNumber, CancellationToken ct) =>
        Run<List<ExecutionPlanSummaryResponse>>(async () => Ok(await _application.GetByCardAsync(cardNumber, ct)));

    /// <summary>Plano mais recente do card, completo (204 quando o card não tem plano).</summary>
    [HttpGet("card/{cardNumber}/current")]
    public Task<ActionResult<ExecutionPlanResponse>> GetCurrent([FromRoute] string cardNumber, CancellationToken ct) =>
        Run<ExecutionPlanResponse>(async () =>
        {
            var plan = await _application.GetCurrentAsync(cardNumber, ct);
            return plan is null ? NoContent() : Ok(plan);
        });

    [HttpGet("{id:guid}")]
    public Task<ActionResult<ExecutionPlanResponse>> Get([FromRoute] Guid id, CancellationToken ct) =>
        Run<ExecutionPlanResponse>(async () => Ok(await _application.GetAsync(id, ct)));

    /// <summary>Define/refina as etapas (upsert pela chave, na ordem enviada).</summary>
    [HttpPut("{id:guid}/steps")]
    public Task<ActionResult<ExecutionPlanResponse>> UpsertSteps([FromRoute] Guid id, [FromBody] UpsertExecutionStepsRequest request, CancellationToken ct) =>
        Run<ExecutionPlanResponse>(async () => Ok(await _application.UpsertStepsAsync(id, request, await GetActorAsync(ct, executor: true), ct)));

    /// <summary>Atualiza uma etapa (status, atividade atual, checkpoint...). Etapa nova é criada no fim.</summary>
    [HttpPatch("{id:guid}/steps/{key}")]
    public Task<ActionResult<ExecutionStepResponse>> UpdateStep([FromRoute] Guid id, [FromRoute] string key, [FromBody] UpdateExecutionStepRequest request,
        [FromServices] solvace.knowledge.application.Contracts.IReverseEngineeringApplication reverse, CancellationToken ct) =>
        Run<ExecutionStepResponse>(async () =>
        {
            // 0052: a skill concluindo a investigação de card com engenharia reversa completa precisa ter consultado/citado a base.
            if (string.Equals(request.Status, ExecutionStatus.Completed, StringComparison.OrdinalIgnoreCase) && IsExecutorRequest())
            {
                var plan = await _application.GetAsync(id, ct);
                if (await reverse.CheckGateAsync(plan.CardNumber, key, request.Message ?? request.Reason, ct) is { } gate)
                    return BadRequest(new { error = gate });
            }
            return Ok(await _application.UpdateStepAsync(id, key, request, await GetActorAsync(ct, executor: true), ct));
        });

    /// <summary>O usuário cancela (pula) uma etapa que ainda não terminou; a skill pula na próxima checagem.</summary>
    [HttpPost("{id:guid}/steps/{key}/cancel")]
    public Task<ActionResult<ExecutionStepResponse>> CancelStep([FromRoute] Guid id, [FromRoute] string key, [FromBody] CancelExecutionStepRequest request, CancellationToken ct) =>
        Run<ExecutionStepResponse>(async () => Ok(await _application.CancelStepAsync(id, key, request.Reason, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    /// <summary>O usuário começa uma etapa pela tela (típico das etapas executor=user) — 0024.</summary>
    [HttpPost("{id:guid}/steps/{key}/start")]
    public Task<ActionResult<ExecutionStepResponse>> StartStep([FromRoute] Guid id, [FromRoute] string key, CancellationToken ct) =>
        Run<ExecutionStepResponse>(async () => Ok(await _application.StartStepAsync(id, key, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    /// <summary>O usuário conclui uma etapa pela tela (ex.: abriu o chamado, validou em QA) — 0024.</summary>
    [HttpPost("{id:guid}/steps/{key}/complete")]
    public Task<ActionResult<ExecutionStepResponse>> CompleteStep([FromRoute] Guid id, [FromRoute] string key, [FromBody] ExecutionStepActionRequest? request, CancellationToken ct) =>
        Run<ExecutionStepResponse>(async () => Ok(await _application.CompleteStepAsync(id, key, request?.Reason, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    /// <summary>"Já resolvi" (0037): o usuário fez o que a etapa travada esperava dele; a skill tenta de novo.</summary>
    [HttpPost("{id:guid}/steps/{key}/resolve")]
    public Task<ActionResult<ExecutionStepResponse>> ResolveStep([FromRoute] Guid id, [FromRoute] string key, [FromBody] ExecutionStepActionRequest? request, CancellationToken ct) =>
        Run<ExecutionStepResponse>(async () => Ok(await _application.ResolveStepAsync(id, key, request?.Reason, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    /// <summary>Planos ativos do usuário logado com pendência dele (0037) — lista de recentes e título da aba.</summary>
    [HttpGet("pending")]
    public Task<ActionResult<List<ExecutionUserPendingResponse>>> Pending(CancellationToken ct) =>
        Run<List<ExecutionUserPendingResponse>>(async () =>
        {
            var claim = User.FindFirst("ExternalId")?.Value;
            return Guid.TryParse(claim, out var userId)
                ? Ok(await _application.GetUserPendingAsync(userId, ct))
                : Ok(new List<ExecutionUserPendingResponse>());
        });

    /// <summary>A skill pergunta; a etapa ligada fica "aguardando" até responderem (tela ou terminal) — 0024.</summary>
    [HttpPost("{id:guid}/questions")]
    public Task<ActionResult<List<ExecutionQuestionResponse>>> Ask([FromRoute] Guid id, [FromBody] AskExecutionQuestionsRequest request, CancellationToken ct) =>
        Run<List<ExecutionQuestionResponse>>(async () => Ok(await _application.AskAsync(id, request, await GetActorAsync(ct, executor: true), ct)));

    /// <summary>Resposta pela tela (via prmake) ou pela skill com o header de executora (via claude).</summary>
    [HttpPost("{id:guid}/questions/{questionId:guid}/answer")]
    public Task<ActionResult<ExecutionQuestionResponse>> Answer([FromRoute] Guid id, [FromRoute] Guid questionId, [FromBody] AnswerExecutionQuestionRequest request, CancellationToken ct) =>
        Run<ExecutionQuestionResponse>(async () => Ok(await _application.AnswerAsync(id, questionId, request.Answer, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    [HttpPost("{id:guid}/questions/{questionId:guid}/cancel")]
    public Task<ActionResult<ExecutionQuestionResponse>> CancelQuestion([FromRoute] Guid id, [FromRoute] Guid questionId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CancelExecutionQuestionRequest? request, CancellationToken ct) =>
        Run<ExecutionQuestionResponse>(async () => Ok(await _application.CancelQuestionAsync(id, questionId, request?.Reason, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    /// <summary>Cancela várias perguntas (número ou id) com o motivo — a skill, quando uma nova rodada as deixa sem sentido.</summary>
    [HttpPost("{id:guid}/questions/cancel")]
    public Task<ActionResult<List<ExecutionQuestionResponse>>> CancelQuestions([FromRoute] Guid id, [FromBody] CancelExecutionQuestionsRequest request, CancellationToken ct) =>
        Run<List<ExecutionQuestionResponse>>(async () => Ok(await _application.CancelQuestionsAsync(id, request, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    /// <summary>Anexa um link à etapa (chamado, PR, documento). Chamado com blocksStep deixa a etapa aguardando — 0024.</summary>
    [HttpPost("{id:guid}/steps/{key}/links")]
    public Task<ActionResult<ExecutionLinkResponse>> AddLink([FromRoute] Guid id, [FromRoute] string key, [FromBody] AddExecutionLinkRequest request, CancellationToken ct) =>
        Run<ExecutionLinkResponse>(async () => Ok(await _application.AddLinkAsync(id, key, request, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    /// <summary>Chamado: o usuário marca aberto/resolvido/fechado (o PRMake não lê o Freshservice).</summary>
    [HttpPatch("{id:guid}/links/{linkId:guid}")]
    public Task<ActionResult<ExecutionLinkResponse>> UpdateLink([FromRoute] Guid id, [FromRoute] Guid linkId, [FromBody] UpdateExecutionLinkRequest request, CancellationToken ct) =>
        Run<ExecutionLinkResponse>(async () => Ok(await _application.UpdateLinkAsync(id, linkId, request, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    [HttpDelete("{id:guid}/links/{linkId:guid}")]
    public Task<ActionResult> DeleteLink([FromRoute] Guid id, [FromRoute] Guid linkId, CancellationToken ct) =>
        RunPlain(async () =>
        {
            await _application.DeleteLinkAsync(id, linkId, await GetActorAsync(ct, executor: IsExecutorRequest()), ct);
            return NoContent();
        });

    /// <summary>Pausar/continuar/cancelar (tela) ou retomar/concluir/falhar (skill, com o header de executora).</summary>
    [HttpPost("{id:guid}/status")]
    public Task<ActionResult<ExecutionPlanResponse>> ChangeStatus([FromRoute] Guid id, [FromBody] ChangeExecutionPlanStatusRequest request, CancellationToken ct) =>
        Run<ExecutionPlanResponse>(async () => Ok(await _application.ChangeStatusAsync(id, request, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    /// <summary>Heartbeat da skill: devolve se ela segue (continue), espera (wait) ou para (stop).</summary>
    [HttpPost("{id:guid}/control")]
    public Task<ActionResult<ExecutionControlResponse>> Control([FromRoute] Guid id, CancellationToken ct) =>
        Run<ExecutionControlResponse>(async () => Ok(await _application.ControlAsync(id, ct)));

    /// <summary>Lote de pedaços de andamento (o mesmo clientId não duplica).</summary>
    [HttpPost("{id:guid}/logs")]
    public Task<ActionResult<object>> AppendLogs([FromRoute] Guid id, [FromBody] AppendExecutionLogsRequest request, CancellationToken ct) =>
        Run<object>(async () => Ok(new { appended = await _application.AppendLogsAsync(id, request, ct) }));

    [HttpGet("{id:guid}/logs")]
    public Task<ActionResult<List<ExecutionLogResponse>>> GetLogs([FromRoute] Guid id, [FromQuery] long afterId = 0,
        [FromQuery] string? stepKey = null, [FromQuery] int limit = 500, CancellationToken ct = default) =>
        Run<List<ExecutionLogResponse>>(async () => Ok(await _application.GetLogsAsync(id, afterId, stepKey, limit, ct)));

    /// <summary>Envia um arquivo (multipart: file, kind, stepKey, description, name). Mesmo tipo+nome substitui.</summary>
    [HttpPost("{id:guid}/artifacts")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public Task<ActionResult<ExecutionArtifactResponse>> UploadArtifact([FromRoute] Guid id, IFormFile? file,
        [FromForm] string? kind, [FromForm] string? stepKey, [FromForm] string? description, [FromForm] string? name,
        CancellationToken ct) =>
        Run<ExecutionArtifactResponse>(async () =>
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "Envie o arquivo no campo 'file'." });

            var fileName = string.IsNullOrWhiteSpace(name) ? file.FileName : name;
            using var buffer = new MemoryStream((int)Math.Min(file.Length, ExecutionArtifact.MaxFileBytes + 1));
            await file.CopyToAsync(buffer, ct);

            var upload = new ExecutionArtifactUpload(fileName, kind, stepKey, description, ResolveContentType(fileName, file.ContentType), buffer.ToArray());
            return Ok(await _application.UploadArtifactAsync(id, upload, await GetActorAsync(ct, executor: IsExecutorRequest()), ct));
        });

    /// <summary>Conteúdo de um arquivo; <c>download=true</c> baixa com o nome original.</summary>
    [HttpGet("{id:guid}/artifacts/{artifactId:guid}/content")]
    public Task<ActionResult> GetArtifactContent([FromRoute] Guid id, [FromRoute] Guid artifactId, [FromQuery] bool download = false, CancellationToken ct = default) =>
        RunPlain(async () =>
        {
            var file = await _application.GetArtifactFileAsync(id, artifactId, ct);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (download)
                return File(file.Data, file.Artifact.ContentType, file.Artifact.Name);
            Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(file.Artifact.Name)}";
            return File(file.Data, file.Artifact.ContentType);
        });

    /// <summary>Todos os arquivos do plano num .zip (uma pasta por tipo).</summary>
    [HttpGet("{id:guid}/artifacts/zip")]
    public Task<ActionResult> DownloadZip([FromRoute] Guid id, CancellationToken ct) =>
        RunPlain(async () =>
        {
            var (fileName, data) = await _application.BuildZipAsync(id, ct);
            return File(data, "application/zip", fileName);
        });

    [HttpDelete("{id:guid}/artifacts/{artifactId:guid}")]
    public Task<ActionResult> DeleteArtifact([FromRoute] Guid id, [FromRoute] Guid artifactId, CancellationToken ct) =>
        RunPlain(async () =>
        {
            await _application.DeleteArtifactAsync(id, artifactId, ct);
            return NoContent();
        });

    // ── 0031: comentários com anexos ──────────────────────────────────────────────────────────────

    /// <summary>Comentários do card (análise e correção), com os anexos — a skill lê daqui.</summary>
    [HttpGet("card/{cardNumber}/notes")]
    public Task<ActionResult<List<ExecutionNoteResponse>>> GetNotes([FromRoute] string cardNumber, CancellationToken ct) =>
        Run<List<ExecutionNoteResponse>>(async () => Ok(await _application.GetNotesByCardAsync(cardNumber, ct, IsExecutorRequest())));

    /// <summary>Novo comentário (multipart: text, stepKey, files — vários). Anexos nunca substituem outros arquivos.</summary>
    [HttpPost("{id:guid}/notes")]
    [RequestSizeLimit(MaxNoteRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxNoteRequestBytes)]
    public Task<ActionResult<ExecutionNoteResponse>> AddNote([FromRoute] Guid id, [FromForm] string? text, [FromForm] string? stepKey,
        [FromForm] List<IFormFile>? files, CancellationToken ct) =>
        Run<ExecutionNoteResponse>(async () =>
        {
            var uploads = new List<ExecutionArtifactUpload>();
            foreach (var file in files ?? [])
            {
                if (file.Length == 0) continue;
                using var buffer = new MemoryStream((int)Math.Min(file.Length, ExecutionArtifact.MaxFileBytes + 1));
                await file.CopyToAsync(buffer, ct);
                uploads.Add(new ExecutionArtifactUpload(file.FileName, null, stepKey, null, ResolveContentType(file.FileName, file.ContentType), buffer.ToArray()));
            }
            return Ok(await _application.AddNoteAsync(id, text, stepKey, uploads, await GetActorAsync(ct, executor: IsExecutorRequest()), ct));
        });

    [HttpPatch("{id:guid}/notes/{noteId:guid}")]
    public Task<ActionResult<ExecutionNoteResponse>> EditNote([FromRoute] Guid id, [FromRoute] Guid noteId, [FromBody] EditExecutionNoteRequest request, CancellationToken ct) =>
        Run<ExecutionNoteResponse>(async () => Ok(await _application.EditNoteAsync(id, noteId, request.Text, await GetActorAsync(ct, executor: IsExecutorRequest()), ct)));

    [HttpDelete("{id:guid}/notes/{noteId:guid}")]
    public Task<ActionResult> DeleteNote([FromRoute] Guid id, [FromRoute] Guid noteId, CancellationToken ct) =>
        RunPlain(async () =>
        {
            await _application.DeleteNoteAsync(id, noteId, await GetActorAsync(ct, executor: IsExecutorRequest()), ct);
            return NoContent();
        });

    // ── 0033: sessão do Claude Code, custo e "continuar" ─────────────────────────────────────────

    /// <summary>A skill informa a sessão do Claude Code (CLAUDE_CODE_SESSION_ID), a máquina e a pasta.</summary>
    [HttpPut("{id:guid}/session")]
    public Task<ActionResult<ExecutionSessionResponse>> RegisterSession([FromRoute] Guid id, [FromBody] RegisterExecutionSessionRequest request, CancellationToken ct) =>
        Run<ExecutionSessionResponse>(async () => Ok(await _application.RegisterSessionAsync(id, request, ct)));

    /// <summary>Custo acumulado da sessão (totais do transcript).</summary>
    [HttpPut("{id:guid}/usage")]
    public Task<ActionResult<ExecutionUsageResponse>> RecordUsage([FromRoute] Guid id, [FromBody] RecordExecutionUsageRequest request, CancellationToken ct) =>
        Run<ExecutionUsageResponse>(async () => Ok(await _application.RecordUsageAsync(id, request, ct)));

    /// <summary>"Continuar" pela tela: o vigia local da máquina da sessão retoma a conversa do Claude.</summary>
    [HttpPost("{id:guid}/resume-request")]
    public Task<ActionResult<ExecutionPlanSummaryResponse>> RequestResume([FromRoute] Guid id, CancellationToken ct) =>
        Run<ExecutionPlanSummaryResponse>(async () => Ok(await _application.RequestResumeAsync(id, await GetActorAsync(ct, executor: false), ct)));

    /// <summary>O vigia/skill pegou o pedido de "continuar".</summary>
    [HttpPost("{id:guid}/resume-ack")]
    public Task<ActionResult> AcknowledgeResume([FromRoute] Guid id, CancellationToken ct) =>
        RunPlain(async () =>
        {
            await _application.AcknowledgeResumeAsync(id, ct);
            return NoContent();
        });

    /// <summary>Planos do usuário que o vigia desta máquina deve retomar (pedido de continuar ou respostas pela tela).</summary>
    [HttpGet("resume-candidates")]
    public Task<ActionResult<List<ExecutionResumeCandidateResponse>>> ResumeCandidates([FromQuery] string? host, CancellationToken ct) =>
        Run<List<ExecutionResumeCandidateResponse>>(async () =>
        {
            var claim = User.FindFirst("ExternalId")?.Value;
            Guid? userId = Guid.TryParse(claim, out var id) ? id : null;
            return Ok(await _application.GetResumeCandidatesAsync(userId, host, ct));
        });

    /// <summary>
    /// 0041: consumo médio por plano com MCP × sem MCP. Só os planos do usuário; admin com <c>all=true</c> vê de todos.
    /// </summary>
    [HttpGet("usage-report")]
    public Task<ActionResult<ExecutionUsageReportResponse>> UsageReport([FromQuery] int days = 30, [FromQuery] bool all = false, CancellationToken ct = default) =>
        Run<ExecutionUsageReportResponse>(async () =>
        {
            var claim = User.FindFirst("ExternalId")?.Value;
            Guid? userId = Guid.TryParse(claim, out var id) ? id : null;
            if (all && !User.IsInRole("admin"))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Só o admin vê o consumo de todos." });
            if (!all && userId is null)
                return BadRequest(new { error = "Seu usuário não tem identificação no PRMake." });
            return Ok(await _application.GetUsageReportAsync(all ? null : userId, days, ct));
        });

    private bool IsExecutorRequest() =>
        string.Equals(Request.Headers[ExecutorHeader].ToString(), "skill", StringComparison.OrdinalIgnoreCase);

    private async Task<ExecutionActor> GetActorAsync(CancellationToken ct, bool executor)
    {
        var claim = User.FindFirst("ExternalId")?.Value;
        Guid? userId = !string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out var id) ? id : null;

        string? name = null;
        if (userId.HasValue)
            name = await _users.GetFullNameAsync(userId.Value, ct);
        name ??= User.FindFirst(ClaimTypes.Name)?.Value;

        return new ExecutionActor(userId, string.IsNullOrWhiteSpace(name) ? "Usuário" : name.Trim(), executor);
    }

    /// <summary>Tipo pelo nome do arquivo (texto legível no visualizador); também usado pelo MCP (0046).</summary>
    internal static string ResolveContentType(string fileName, string? declared)
    {
        if (ContentTypes.TryGetContentType(fileName, out var byExtension))
            return byExtension;
        return string.IsNullOrWhiteSpace(declared) ? "application/octet-stream" : declared;
    }

    private static FileExtensionContentTypeProvider CreateContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        // Texto legível no visualizador (e baixado com a extensão original).
        foreach (var ext in new[] { ".sql", ".sh", ".bash", ".py", ".ps1", ".cs", ".ts", ".log", ".tsv", ".yaml", ".yml" })
            provider.Mappings[ext] = "text/plain; charset=utf-8";
        provider.Mappings[".md"] = "text/markdown; charset=utf-8";
        provider.Mappings[".txt"] = "text/plain; charset=utf-8";
        provider.Mappings[".csv"] = "text/csv; charset=utf-8";
        provider.Mappings[".json"] = "application/json; charset=utf-8";
        return provider;
    }

    private async Task<ActionResult<T>> Run<T>(Func<Task<ActionResult>> action)
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

    private async Task<ActionResult> RunPlain(Func<Task<ActionResult>> action)
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
        ExecutionPlanNotFoundException => NotFound(new { error = e.Message }),
        ExecutionPlanConcurrencyException => Conflict(new { error = "O plano foi alterado ao mesmo tempo por outra ação. Tente de novo." }),
        _ => null
    };
}
