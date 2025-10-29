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
public class FormController : ControllerBase
{
    private readonly IFormApplication _application;

    public FormController(IFormApplication application)
    {
        _application = application;
    }
    
    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<FormResponse>> Create(FormRequest request, CancellationToken cancellationToken)
    {
        var userCreated = await _application.Create(request, cancellationToken);
        
        return Ok(userCreated);
    }
    
    [HttpGet]
    public async Task<ActionResult<FormResponse>> GetForm(int id, CancellationToken cancellationToken)
    {
        var user = await _application.Get(id, cancellationToken);
        
        return Ok(user);
    }
    
    [HttpGet("GetByEnvironment")]
    public async Task<ActionResult<FormResponse>> GetByEnvironment(string enrironmentName, CancellationToken cancellationToken)
    {
        var user = await _application.GetByEnvironment(enrironmentName, cancellationToken);
        
        return Ok(user);
    }
    
    [HttpGet("GetForms")]
    public async Task<ActionResult<IEnumerable<FormResponse>>> GetForms(CancellationToken cancellationToken)
    {
        var user = await _application.GetForms( cancellationToken);
        
        return Ok(user);
    }
}