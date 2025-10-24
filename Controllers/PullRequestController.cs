using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using solvace.prform.Data.Contexts;
using solvace.prform.Data.Entities;
using solvace.prform.Data.Requests;
using solvace.prform.Data.Responses;

namespace solvace.prform.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PullRequestController : ControllerBase
{
    private readonly DefaultContext _context;

    public PullRequestController(DefaultContext context)
    {
        _context = context;
    }
    
    [HttpPost]
    public async Task<ActionResult<PullRequestRegisterResponse>> Create(PullRequestRegisterRequest request, CancellationToken cancellationToken)
    {
        var requestExists = await _context.PullRequests
            .FirstOrDefaultAsync(x => x.CardNumber == request.CardNumber && x.UserId == request.UserId, cancellationToken);

        if (requestExists is not null)
        {
            requestExists.SetDescription(request.Description);
            requestExists.SetRootCause(request.RootCause);
            _context.PullRequests.Update(requestExists);
            
            if(await _context.SaveChangesAsync(cancellationToken) > 0)
                return Ok(new PullRequestRegisterResponse(){Id = requestExists.Id});
        }
        var requestRegister = request.Create(request);
        
        var response = await _context.PullRequests.AddAsync(requestRegister, cancellationToken);
        if(await _context.SaveChangesAsync(cancellationToken) > 0)
            return Ok(new PullRequestRegisterResponse(){Id = response.Entity.Id});

        return UnprocessableEntity();
    }
    
    [HttpGet]
    public async Task<ActionResult<PullRequestRegisterResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var user = await _context.PullRequests.Where(x => x.Id == id).FirstAsync(cancellationToken);
        if (user == null) throw new ArgumentNullException(nameof(user));

        return Ok(user);
    }
    
    [HttpGet("GetByEnvironmentNameAndCardNumber")]
    public async Task<ActionResult<PullRequestRegisterResponse>> GetByEnvironmentNameAndCardNumber(string environmentName, string cardNumber, int userId, CancellationToken cancellationToken)
    {
        var response = await _context.PullRequests
            .Where(x => x.Form!.EnvironmentName.ToLower() == environmentName.ToLower() && x.CardNumber == cardNumber && x.UserId == userId)
            .Include(x => x.Form)
            .Include(x => x.User)
            .FirstOrDefaultAsync(cancellationToken);
        
        if(response == null)
            return NotFound();
        
        return Ok(response);
    }
}