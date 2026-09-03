using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class HandoverController : ControllerBase
{
    private readonly IHandoverApplication _application;

    public HandoverController(IHandoverApplication application)
    {
        _application = application;
    }

    // Uso autenticado (modal do handover): retorna o handover independente da visibilidade.
    [HttpGet("GetByCardNumber")]
    public async Task<ActionResult<HandoverResponse>> GetByCardNumber(string cardNumber, CancellationToken cancellationToken)
    {
        var response = await _application.GetByCardNumber(cardNumber, cancellationToken);
        return Ok(response);
    }

    // Link público (somente leitura), sem autenticação. Só libera quando IsPublic = true.
    // 404 quando não existe; 403 quando existe mas não é público.
    [AllowAnonymous]
    [HttpGet("public/{cardNumber}")]
    public async Task<ActionResult<HandoverResponse>> GetPublicByCardNumber(string cardNumber, CancellationToken cancellationToken)
    {
        var response = await _application.GetByCardNumber(cardNumber, cancellationToken);

        if (response is null)
            return NotFound();

        if (!response.IsPublic)
            return StatusCode(StatusCodes.Status403Forbidden, "Este handover não é público.");

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<HandoverResponse>> Save(HandoverRequest request, CancellationToken cancellationToken)
    {
        var response = await _application.Save(request, cancellationToken);
        return Ok(response);
    }

    // Habilita/desabilita o acesso público (somente autenticado).
    [HttpPut("{cardNumber}/visibility")]
    public async Task<ActionResult<HandoverResponse>> SetVisibility(string cardNumber, [FromBody] HandoverVisibilityRequest request, CancellationToken cancellationToken)
    {
        var response = await _application.SetVisibility(cardNumber, request.IsPublic, cancellationToken);

        if (response is null)
            return NotFound();

        return Ok(response);
    }
}
