using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using solvace.prform.Data.Contexts;
using solvace.prform.Data.Entities;
using solvace.prform.Data.Requests;
using solvace.prform.Data.Responses;

namespace solvace.prform.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly DefaultContext _context;

    public UserController(DefaultContext context)
    {
        _context = context;
    }
    
    [HttpPost]
    public async Task<ActionResult<UserResponse>> CreateUser(UserRequest request, CancellationToken cancellationToken)
    {
        var user = request.CreateUser(request.Name);
        
        var userCreated = await _context.Users.AddAsync(user, cancellationToken);
        if(await _context.SaveChangesAsync(cancellationToken) > 0)
            return Ok(userCreated.Entity.ToResponse());

        return UnprocessableEntity();
    }
    
    [HttpGet]
    public async Task<ActionResult<UserResponse>> GetUser(int id, CancellationToken cancellationToken)
    {
        var user = await _context.Users.Where(x => x.Id == id).FirstAsync(cancellationToken);
        if (user == null) throw new ArgumentNullException(nameof(user));

        return Ok(user);
    }
    
    [HttpGet("GetUsers")]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetUsers(CancellationToken cancellationToken)
    {
        var users = await _context.Users.ToListAsync(cancellationToken);
        
        if(users.Count == 0)
            return NotFound();
        
        return Ok(users.Select(x => x.ToResponse()));
    }
}