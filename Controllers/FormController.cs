using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using solvace.prform.Data.Contexts;
using solvace.prform.Data.Entities;
using solvace.prform.Data.Requests;
using solvace.prform.Data.Responses;

namespace solvace.prform.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FormController : ControllerBase
{
    private readonly DefaultContext _context;

    public FormController(DefaultContext context)
    {
        _context = context;
    }
    
    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<FormResponse>> Create(FormRequest request, CancellationToken cancellationToken)
    {
        var formRequest = request.CreateForm(request);
        
        var userCreated = await _context.Forms.AddAsync(formRequest, cancellationToken);
        
        if(await _context.SaveChangesAsync(cancellationToken) > 0)
            return Ok(userCreated.Entity.ToResponse());

        return UnprocessableEntity();
    }
    
    [HttpGet]
    public async Task<ActionResult<FormResponse>> GetForm(int id, CancellationToken cancellationToken)
    {
        var user = await _context.Forms.Where(x => x.Id == id).FirstAsync(cancellationToken);
        
        if (user == null) throw new ArgumentNullException(nameof(user));

        return Ok(user);
    }
    
    [HttpGet("GetByEnvironment")]
    public async Task<ActionResult<FormResponse>> GetByEnvironment(string enrironmentName, CancellationToken cancellationToken)
    {
        var user = await _context.Forms.Where(x => x.EnvironmentName == enrironmentName).FirstAsync(cancellationToken);
        
        if (user == null) throw new ArgumentNullException(nameof(user));

        return Ok(user);
    }
    
    [HttpGet("GetForms")]
    public async Task<ActionResult<IEnumerable<FormResponse>>> GetForms(CancellationToken cancellationToken)
    {
        var users = await _context.Forms.ToListAsync(cancellationToken);
        
        if(users.Count == 0)
            return NotFound();
        
        return Ok(users.Select(x => x.ToResponse()));
    }
}