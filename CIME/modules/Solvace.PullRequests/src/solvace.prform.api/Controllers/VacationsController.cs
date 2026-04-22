using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.vacations.application.Contracts;
using solvace.vacations.domain.Requests;
using solvace.vacations.domain.Responses;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Roles = "admin, user, gestor, support")]
public class VacationsController : ControllerBase
{
    private readonly IVacationApplication _vacationApplication;

    public VacationsController(IVacationApplication vacationApplication)
    {
        _vacationApplication = vacationApplication;
    }

    /// <summary>
    /// Cria uma nova solicitação de férias
    /// </summary>
    [HttpPost("request")]
    public async Task<ActionResult<VacationRequestResponse>> CreateVacationRequest(
        [FromBody] CreateVacationRequestRequest request,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst("ExternalId")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid user ID");

        request.UserId = userId;
        var result = await _vacationApplication.CreateVacationRequestAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Atualiza uma solicitação de férias (apenas se ainda não foi aprovada pelo gestor)
    /// </summary>
    [HttpPut("request/{id:int}")]
    public async Task<ActionResult<VacationRequestResponse>> UpdateVacationRequest(
        [FromRoute] int id,
        [FromBody] UpdateVacationRequestRequest request,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst("ExternalId")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid user ID");

        var result = await _vacationApplication.UpdateVacationRequestAsync(id, request, userId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Obtém uma solicitação de férias específica
    /// </summary>
    [HttpGet("request/{id:int}")]
    public async Task<ActionResult<VacationRequestResponse>> GetVacationRequest(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var result = await _vacationApplication.GetVacationRequestAsync(id, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Obtém todas as solicitações de férias do usuário logado
    /// </summary>
    [HttpGet("my-requests")]
    public async Task<ActionResult<List<VacationRequestResponse>>> GetMyVacationRequests(
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst("ExternalId")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid user ID");

        var result = await _vacationApplication.GetUserVacationRequestsAsync(userId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Obtém todas as solicitações de férias de todos os usuários
    /// </summary>
    [HttpGet("all-requests")]
    public async Task<ActionResult<List<VacationRequestResponse>>> GetAllVacationRequests(
        CancellationToken cancellationToken)
    {
        var result = await _vacationApplication.GetAllVacationRequestsAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Aprova uma solicitação de férias (apenas gestores)
    /// </summary>
    [HttpPost("request/{id:int}/approve")]
    [Authorize(Roles = "gestor, admin")]
    public async Task<ActionResult<VacationRequestResponse>> ApproveVacationRequest(
        [FromRoute] int id,
        [FromBody] ApproveVacationRequest request,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst("ExternalId")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var managerId))
            return Unauthorized("Invalid user ID");

        request.ManagerId = managerId;
        var result = await _vacationApplication.ApproveByManagerAsync(id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Autoriza uma solicitação de férias pelo RH (apenas gestores)
    /// </summary>
    [HttpPost("request/{id:int}/authorize")]
    [Authorize(Roles = "gestor, admin")]
    public async Task<ActionResult<VacationRequestResponse>> AuthorizeVacationRequest(
        [FromRoute] int id,
        [FromBody] AuthorizeVacationRequest request,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst("ExternalId")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var hrId))
            return Unauthorized("Invalid user ID");

        request.HRId = hrId;
        var result = await _vacationApplication.AuthorizeByHRAsync(id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Exclui uma solicitação de férias
    /// Usuário pode excluir apenas se ainda não foi aprovada
    /// Gestor pode excluir qualquer solicitação não concluída
    /// </summary>
    [HttpDelete("request/{id:int}")]
    public async Task<ActionResult> DeleteVacationRequest(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst("ExternalId")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid user ID");

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var isManager = roles.Contains("gestor") || roles.Contains("admin");

        await _vacationApplication.DeleteVacationRequestAsync(id, userId, isManager, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Obtém o calendário de férias para um mês/ano específico
    /// Mostra as datas ocupadas e por quem
    /// </summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<List<CalendarDayResponse>>> GetCalendar(
        [FromQuery] int month,
        [FromQuery] int year,
        CancellationToken cancellationToken)
    {
        if (month < 1 || month > 12)
            return BadRequest("Invalid month");

        if (year < 2000 || year > 2100)
            return BadRequest("Invalid year");

        var result = await _vacationApplication.GetCalendarAsync(month, year, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Cria o saldo de férias para um usuário (apenas admin)
    /// </summary>
    [HttpPost("balance")]
    [Authorize(Roles = "admin,support")]
    public async Task<ActionResult<UserVacationBalanceResponse>> CreateUserVacationBalance(
        [FromBody] CreateUserVacationBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _vacationApplication.CreateUserVacationBalanceAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Obtém o saldo de férias do usuário logado para um ano específico
    /// </summary>
    [HttpGet("balance")]
    public async Task<ActionResult<UserVacationBalanceResponse>> GetMyVacationBalance(
        [FromQuery] int year,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst("ExternalId")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid user ID");

        if (year < 2000 || year > 2100)
            return BadRequest("Invalid year");

        var result = await _vacationApplication.GetUserVacationBalanceAsync(userId, year, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Obtém o saldo de férias de um usuário específico (apenas gestores e admin)
    /// </summary>
    [HttpGet("balance/{userId:guid}")]
    [Authorize(Roles = "gestor, admin")]
    public async Task<ActionResult<UserVacationBalanceResponse>> GetUserVacationBalance(
        [FromRoute] Guid userId,
        [FromQuery] int year,
        CancellationToken cancellationToken)
    {
        if (year < 2000 || year > 2100)
            return BadRequest("Invalid year");

        var result = await _vacationApplication.GetUserVacationBalanceAsync(userId, year, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Obtém todos os saldos de férias do usuário logado (todos os períodos aquisitivos)
    /// </summary>
    [HttpGet("balances")]
    public async Task<ActionResult<List<UserVacationBalanceResponse>>> GetMyVacationBalances(
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst("ExternalId")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid user ID");

        var result = await _vacationApplication.GetUserVacationBalancesAsync(userId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Obtém todos os saldos de férias de um usuário específico (apenas gestores e admin)
    /// </summary>
    [HttpGet("balances/{userId:guid}")]
    [Authorize(Roles = "gestor, admin")]
    public async Task<ActionResult<List<UserVacationBalanceResponse>>> GetUserVacationBalances(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await _vacationApplication.GetUserVacationBalancesAsync(userId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Atualiza um saldo de férias (apenas admin)
    /// </summary>
    [HttpPut("balance/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<UserVacationBalanceResponse>> UpdateUserVacationBalance(
        [FromRoute] int id,
        [FromBody] UpdateUserVacationBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _vacationApplication.UpdateUserVacationBalanceAsync(id, request, cancellationToken);
        return Ok(result);
    }
}
