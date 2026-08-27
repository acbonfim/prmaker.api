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

    [HttpGet("GetByCardNumber")]
    public async Task<ActionResult<HandoverResponse>> GetByCardNumber(string cardNumber, CancellationToken cancellationToken)
    {
        var response = await _application.GetByCardNumber(cardNumber, cancellationToken);
        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<HandoverResponse>> Save(HandoverRequest request, CancellationToken cancellationToken)
    {
        var response = await _application.Save(request, cancellationToken);
        return Ok(response);
    }
}
