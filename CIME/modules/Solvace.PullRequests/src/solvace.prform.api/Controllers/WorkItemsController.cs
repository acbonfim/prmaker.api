using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;

namespace solvace.prform.Controllers;

/// <summary>
/// 0085: itens de trabalho do card — vários por card, cada um com sub-tarefas ordenadas (executar, cancelar, desfazer).
/// A tela (Bancada do card), a skill (header <c>X-Execution-Client: skill</c>), o executor e o especialista (token do
/// caso) usam as mesmas rotas. O catálogo (<c>GET catalog</c>) fica em <c>WorkItemsController.Catalog.cs</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public partial class WorkItemsController : ExecutionControllerBase
{
    private readonly IWorkItemApplication _items;
    private readonly solvace.timeline.application.Contracts.IUserRepository _users;

    public WorkItemsController(IWorkItemApplication items, solvace.timeline.application.Contracts.IUserRepository users) : base(users)
    {
        _items = items;
        _users = users;
    }

    /// <summary>Itens do card (com sub-tarefas), em ordem de número.</summary>
    [HttpGet("card/{card}")]
    public Task<ActionResult<IReadOnlyList<WorkItemResponse>>> List([FromRoute] string card, CancellationToken ct) =>
        RunItem<IReadOnlyList<WorkItemResponse>>(async () => Ok(await _items.ListAsync(card, ct)));

    /// <summary>Item com sub-tarefas e histórico.</summary>
    [HttpGet("{number:long}")]
    public Task<ActionResult<WorkItemResponse>> Get([FromRoute] long number, CancellationToken ct) =>
        RunItem<WorkItemResponse>(async () => Ok(await _items.GetAsync(number, withLogs: true, ct)));

    /// <summary>Cria um item (201). Igual a outro do card → 409 <c>{code:"duplicate", existing}</c>.</summary>
    [HttpPost("card/{card}")]
    public Task<ActionResult<WorkItemResponse>> Create([FromRoute] string card, [FromBody] CreateWorkItemRequest request, CancellationToken ct) =>
        RunItem<WorkItemResponse>(async () =>
        {
            EnsureSpecialistCard(card);
            var created = await _items.CreateAsync(card, request, OriginOf(request), await ActorAsync(ct), ct);
            return StatusCode(StatusCodes.Status201Created, created);
        });

    /// <summary>Edita (só item pendente com sub-tarefas pendentes; senão 409 <c>{code:"notEditable"}</c>).</summary>
    [HttpPut("{number:long}")]
    public Task<ActionResult<WorkItemResponse>> Update([FromRoute] long number, [FromBody] UpdateWorkItemRequest request, CancellationToken ct) =>
        RunItem<WorkItemResponse>(async () =>
        {
            await EnsureSpecialistItemAsync(number, ct);
            return Ok(await _items.UpdateAsync(number, request, await ActorAsync(ct), ct));
        });

    /// <summary>"Criar novo a partir deste": cópia pendente.</summary>
    [HttpPost("{number:long}/duplicate")]
    public Task<ActionResult<WorkItemResponse>> Duplicate([FromRoute] long number, CancellationToken ct) =>
        RunItem<WorkItemResponse>(async () => Ok(await _items.DuplicateAsync(number, await ActorAsync(ct), ct)));

    /// <summary>Executa: item avulso entra na fila (pista própria); item de plano retoma o plano.</summary>
    [HttpPost("{number:long}/execute")]
    public Task<ActionResult<WorkItemExecuteResponse>> Execute([FromRoute] long number, CancellationToken ct) =>
        RunItem<WorkItemExecuteResponse>(async () =>
        {
            var item = await _items.ExecuteAsync(number, await ActorAsync(ct), ct);
            return Ok(new WorkItemExecuteResponse { RequestId = item.RequestId, Status = item.Status, Item = item });
        });

    /// <summary>Cancela (sub-tarefas abertas e pedido na fila); <c>undo=true</c> desfaz o que já foi feito.</summary>
    [HttpPost("{number:long}/cancel")]
    public Task<ActionResult<WorkItemResponse>> Cancel([FromRoute] long number,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CancelWorkItemRequest? request, CancellationToken ct) =>
        RunItem<WorkItemResponse>(async () =>
            Ok(await _items.CancelAsync(number, request?.Undo ?? false, request?.Reason, await ActorAsync(ct), ct)));

    /// <summary>Desfaz item já cancelado/concluído (sub-tarefas concluídas em ordem reversa).</summary>
    [HttpPost("{number:long}/undo")]
    public Task<ActionResult<WorkItemResponse>> Undo([FromRoute] long number, CancellationToken ct) =>
        RunItem<WorkItemResponse>(async () => Ok(await _items.UndoAsync(number, await ActorAsync(ct), ct)));

    /// <summary>Ação na sub-tarefa: start | complete | fail | wait | skip | revert | cancel (usuário ou executor).</summary>
    [HttpPost("tasks/{taskNumber:long}/{verb}")] // "action" é valor de rota reservado do MVC
    public Task<ActionResult<WorkItemResponse>> TaskAction([FromRoute] long taskNumber, [FromRoute] string verb,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] WorkTaskActionRequest? request, CancellationToken ct) =>
        RunItem<WorkItemResponse>(async () =>
            Ok(await _items.TaskActionAsync(taskNumber, verb, request ?? new WorkTaskActionRequest(), await ActorAsync(ct), ct)));

    /// <summary>Materializa o item do plano (cards antigos) — idempotente.</summary>
    [HttpPost("card/{card}/from-plan/{planId:guid}")]
    public Task<ActionResult<WorkItemResponse>> FromPlan([FromRoute] string card, [FromRoute] Guid planId, CancellationToken ct) =>
        RunItem<WorkItemResponse>(async () => Ok(await _items.FromPlanAsync(card, planId, await ActorAsync(ct), ct)));

    // ── Internos ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A skill se identifica com o header (como no plano); o executor pela credencial dele.</summary>
    private bool IsExecutorRequest() =>
        string.Equals(Request.Headers[ExecutionPlanController.ExecutorHeader].ToString(), "skill", StringComparison.OrdinalIgnoreCase)
        || ExecutorClaims.WorkerIdOf(User) is not null;

    private async Task<ExecutionActor> ActorAsync(CancellationToken ct)
    {
        if (CaseClaims.SpecialistOf(User) is { } specialist)
            return specialist.AsExecutionActor();
        var userId = CurrentUserId;
        string? name = null;
        if (userId.HasValue)
            name = await _users.GetFullNameAsync(userId.Value, ct);
        name ??= User.FindFirst(ClaimTypes.Name)?.Value;
        return new ExecutionActor(userId, string.IsNullOrWhiteSpace(name) ? "Usuário" : name.Trim(), IsExecutorRequest());
    }

    /// <summary>Especialista (token do caso) → specialist; skill/executor → fase do plano; tela → user (ou template).</summary>
    private string OriginOf(CreateWorkItemRequest request)
    {
        if (CaseClaims.SpecialistOf(User) is not null)
            return WorkItemOrigin.Specialist;
        if (IsExecutorRequest())
            return string.Equals(request.PlanPhase?.Trim(), ExecutionPhase.Analysis, StringComparison.OrdinalIgnoreCase)
                ? WorkItemOrigin.Analysis
                : WorkItemOrigin.Correction;
        return string.IsNullOrWhiteSpace(request.TemplateKey) ? WorkItemOrigin.User : WorkItemOrigin.Template;
    }

    /// <summary>O especialista só mexe no card do caso dele.</summary>
    private void EnsureSpecialistCard(string card)
    {
        if (CaseClaims.SpecialistOf(User) is not null
            && !string.Equals(CaseClaims.CardOf(User), card?.Trim(), StringComparison.Ordinal))
            throw new ExecutionForbiddenException("O token do caso só vale para o card do caso.");
    }

    private async Task EnsureSpecialistItemAsync(long number, CancellationToken ct)
    {
        if (CaseClaims.SpecialistOf(User) is null) return;
        var item = await _items.GetAsync(number, withLogs: false, ct);
        EnsureSpecialistCard(item.CardNumber);
    }

    /// <summary>Erros dos itens: duplicado e não editável → 409 com código; o resto pelo mapeamento da base.</summary>
    private Task<ActionResult<T>> RunItem<T>(Func<Task<ActionResult>> action) =>
        Run<T>(async () =>
        {
            try
            {
                return await action();
            }
            catch (WorkItemDuplicateException e)
            {
                return Conflict(new
                {
                    code = "duplicate",
                    error = e.Message,
                    existing = new { number = e.ExistingNumber, title = e.ExistingTitle, status = e.ExistingStatus },
                });
            }
            catch (WorkItemNotEditableException e)
            {
                return Conflict(new { code = "notEditable", error = e.Message, number = e.Number });
            }
        });
}
