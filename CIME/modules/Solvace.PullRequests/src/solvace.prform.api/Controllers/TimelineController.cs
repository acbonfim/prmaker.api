using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using solvace.timeline.application.Contracts;
using solvace.timeline.domain.Requests;
using solvace.timeline.domain.Responses;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class TimelineController : ControllerBase
{
    private readonly ITimelineApplication _timelineApplication;

    public TimelineController(ITimelineApplication timelineApplication)
    {
        _timelineApplication = timelineApplication;
    }

    /// <summary>
    /// Cria um novo registro na linha do tempo de um card.
    /// Interno (usuário logado): grava o Id do usuário e o nome obtido da base.
    /// Externo (sem usuário logado): exige o campo 'userName' no corpo e grava sem Id de usuário.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TimelineEntryResponse>> Create(
        [FromBody] CreateTimelineEntryRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null && string.IsNullOrWhiteSpace(request.UserName))
            return BadRequest("O campo 'userName' é obrigatório para registros externos (sem usuário logado).");

        var result = await _timelineApplication.CreateAsync(request, userId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Obtém um registro específico da linha do tempo.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<TimelineEntryResponse>> GetById(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var result = await _timelineApplication.GetByIdAsync(id, cancellationToken);

        if (result is null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Lista todos os registros de um card, ordenados da ocorrência mais recente para a mais antiga.
    /// </summary>
    [HttpGet("card/{cardNumber}")]
    public async Task<ActionResult<List<TimelineEntryResponse>>> GetByCardNumber(
        [FromRoute] string cardNumber,
        CancellationToken cancellationToken)
    {
        var result = await _timelineApplication.GetByCardNumberAsync(cardNumber, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Edita a descrição de um registro.
    /// Registro interno: apenas o autor (ou admin) pode editar.
    /// Registro externo: editável pela via externa (chamador sem usuário logado) ou por admin.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<TimelineEntryResponse>> Update(
        [FromRoute] int id,
        [FromBody] UpdateTimelineEntryRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await _timelineApplication.GetByIdAsync(id, cancellationToken);

        if (entry is null)
            return NotFound();

        if (!CanMutate(entry))
            return StatusCode(StatusCodes.Status403Forbidden, "Você não tem permissão para editar este registro.");

        var updated = await _timelineApplication.UpdateAsync(id, request, GetUserId()?.ToString(), cancellationToken);
        return Ok(updated);
    }

    /// <summary>
    /// Exclui um registro. Segue as mesmas regras de permissão da edição.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var entry = await _timelineApplication.GetByIdAsync(id, cancellationToken);

        if (entry is null)
            return NotFound();

        if (!CanMutate(entry))
            return StatusCode(StatusCodes.Status403Forbidden, "Você não tem permissão para excluir este registro.");

        await _timelineApplication.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst("ExternalId")?.Value;
        return !string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out var id) ? id : null;
    }

    private bool IsAdmin() =>
        User.FindAll(ClaimTypes.Role).Any(c => c.Value == "admin");

    /// <summary>
    /// Regras de permissão para editar/excluir um registro.
    /// </summary>
    private bool CanMutate(TimelineEntryResponse entry)
    {
        if (IsAdmin())
            return true;

        var userId = GetUserId();

        // Registro interno: somente o autor.
        if (entry.UserId.HasValue)
            return userId.HasValue && userId.Value == entry.UserId.Value;

        // Registro externo (sem Id de usuário): editável pela via externa (chamador sem usuário logado).
        return userId is null;
    }
}
