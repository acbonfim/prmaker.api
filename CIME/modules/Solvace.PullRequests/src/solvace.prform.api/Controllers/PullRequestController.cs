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
    public async Task<ActionResult<PullRequestRegisterResponse>> Get(int id, CancellationToken cancellationToken, string? repositoryId = null)
    {
        var user = await _application.Get(id, repositoryId, cancellationToken);

        return Ok(user);
    }

    [HttpGet("GetByCardNumber")]
    public async Task<ActionResult<PullRequestRegisterResponse>> GetByCardNumber(string cardNumber, CancellationToken cancellationToken, string? repositoryId = null)
    {
        var response = await _application.GetByCardNumber(cardNumber, repositoryId, cancellationToken);

        return Ok(response);
    }

    [HttpGet("GetRecentByUser")]
    public async Task<ActionResult<IReadOnlyList<PullRequestRecentResponse>>> GetRecentByUser(Guid userId, CancellationToken cancellationToken, int take = 5)
    {
        var response = await _application.GetRecentByUser(userId, take, cancellationToken);

        return Ok(response);
    }
}