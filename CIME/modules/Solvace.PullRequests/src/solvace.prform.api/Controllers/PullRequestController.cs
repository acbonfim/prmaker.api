using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class PullRequestController : ControllerBase
{
    private readonly IPullRequestApplication _application;

    public PullRequestController(IPullRequestApplication application)
    {
        _application = application;
    }
    
    [HttpPost]
    public async Task<ActionResult<PullRequestRegisterResponse>> Create(PullRequestRegisterRequest request, CancellationToken cancellationToken)
    {
        var created = await _application.Create(request, cancellationToken);
        
        return Ok(created);
    }
    
    [HttpGet]
    public async Task<ActionResult<PullRequestRegisterResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var user = await _application.Get(id, cancellationToken);

        return Ok(user);
    }
    
    [HttpGet("GetByCardNumber")]
    public async Task<ActionResult<PullRequestRegisterResponse>> GetByCardNumber(string environmentName, string cardNumber, int userId, CancellationToken cancellationToken)
    {
        var response = await _application.GetByCardNumber(cardNumber, cancellationToken);
        
        return Ok(response);
    }
}