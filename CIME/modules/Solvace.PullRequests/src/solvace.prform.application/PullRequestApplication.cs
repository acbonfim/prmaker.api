using Microsoft.EntityFrameworkCore;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.application;

public class PullRequestApplication : IPullRequestApplication
{
    private readonly DefaultContext _context;
    private DbSet<PullRequestRegister> _prRepository;
    
    public PullRequestApplication(DefaultContext context)
    {
        _context = context;
        _prRepository = context.PullRequests;
    }
    public async Task<bool> CommitAsync(CancellationToken cancellationToken)
    {
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<PullRequestRegisterResponse> Create(PullRequestRegisterRequest request, CancellationToken cancellationToken)
    {
        var requestExists = await _prRepository
            .FirstOrDefaultAsync(x => x.CardNumber == request.CardNumber && x.RepositoryId == request.RepositoryId, cancellationToken);

        if (requestExists is not null)
        {
            requestExists.SetDescription(request.Description);
            requestExists.SetRootCause(request.RootCause);
            requestExists.BranchName = request.BranchName;
            requestExists.BranchPrefix = request.BranchPrefix;
            requestExists.SetRepositoryId(request.RepositoryId);
            _prRepository.Update(requestExists);
            
            if(await CommitAsync(cancellationToken))
                return new PullRequestRegisterResponse(){Id = requestExists.Id};
        }
        
        var requestRegister = request.Create(request);
        
        var response = await _prRepository.AddAsync(requestRegister, cancellationToken);
        if(await CommitAsync(cancellationToken))
            return new PullRequestRegisterResponse(){Id = response.Entity.Id};
        
        throw new Exception("Error on save");
    }

    public async Task<PullRequestRegisterResponse> Get(int id, string? repositoryId, CancellationToken cancellationToken)
    {
        var query = _prRepository.Where(x => x.Id == id);
        if (repositoryId != null)
            query = query.Where(x => x.RepositoryId == repositoryId);

        var user = await query.FirstAsync(cancellationToken);
        if (user == null) throw new ArgumentNullException(nameof(user));

        return user.ToResponse();
    }

    public async Task<PullRequestRegisterResponse> GetByCardNumber(string cardNumber, string? repositoryId, CancellationToken cancellationToken)
    {
        var query = _prRepository
            .Where(x => x.CardNumber == cardNumber);
        if (repositoryId != null)
            query = query.Where(x => x.RepositoryId == repositoryId);

        var response = await query
            .Include(x => x.Form)
            .FirstOrDefaultAsync(cancellationToken);
        
        if(response == null)
            return null;

        return response.ToResponse();
    }

    public async Task<IReadOnlyList<PullRequestRecentResponse>> GetRecentByUser(Guid userId, int take, CancellationToken cancellationToken)
    {
        if (take <= 0) take = 5;
        if (take > 50) take = 50;

        return await _prRepository
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Take(take)
            .Select(x => new PullRequestRecentResponse
            {
                Id = x.Id,
                CardNumber = x.CardNumber,
                Description = x.Description,
                RepositoryId = x.RepositoryId,
                BranchPrefix = x.BranchPrefix,
                BranchName = x.BranchName,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
            })
            .ToListAsync(cancellationToken);
    }
}