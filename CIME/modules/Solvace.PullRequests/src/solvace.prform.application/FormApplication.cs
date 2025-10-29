using Microsoft.EntityFrameworkCore;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.application;

public class FormApplication : IFormApplication
{
    private readonly DefaultContext _context;
    private DbSet<Form> _formRepository;
    public FormApplication(DefaultContext context)
    {
        _context = context;
        _formRepository = context.Forms;
    }
    
    public async Task<FormResponse> Create(FormRequest request, CancellationToken cancellationToken)
    {
        var formRequest = request.CreateForm(request);
        
        var userCreated = await _formRepository.AddAsync(formRequest, cancellationToken);

        if (await CommitAsync(cancellationToken))
            return userCreated.Entity.ToResponse();
        
        throw new Exception("Error on save");
    }

    public async Task<FormResponse> Get(int id, CancellationToken cancellationToken)
    {
        var user = await _context.Forms.Where(x => x.Id == id).FirstAsync(cancellationToken);
        
        if (user == null) throw new ArgumentNullException(nameof(user));

        return user.ToResponse();
    }

    public async Task<FormResponse> GetByEnvironment(string enrironmentName, CancellationToken cancellationToken)
    {
        var user = await _context.Forms.Where(x => x.EnvironmentName == enrironmentName).FirstAsync(cancellationToken);
        
        if (user == null) throw new ArgumentNullException(nameof(user));

        return user.ToResponse();
    }

    public async Task<IEnumerable<FormResponse>> GetForms(CancellationToken cancellationToken)
    {
        var users = await _context.Forms.ToListAsync(cancellationToken);

        if (users.Count == 0)
            return new List<FormResponse>();
        
        return users.Select(x => x.ToResponse());
    }

    public async Task<bool> CommitAsync(CancellationToken cancellationToken)
    {
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }
}