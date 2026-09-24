using Cime.BuildingBlocks.RealTime;
using Microsoft.EntityFrameworkCore;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.domain.RealTime;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.application;

public class PullRequestApplication : IPullRequestApplication
{
    private readonly DefaultContext _context;
    private readonly IRealTimeNotifier _realTimeNotifier;
    private DbSet<PullRequestRegister> _prRepository;
    
    public PullRequestApplication(DefaultContext context, IRealTimeNotifier realTimeNotifier)
    {
        _context = context;
        _realTimeNotifier = realTimeNotifier;
        _prRepository = context.PullRequests;
    }
    public async Task<bool> CommitAsync(CancellationToken cancellationToken)
    {
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }

    /// <summary>
    /// Upsert do registro do card (um por card). Branch/repositório do request são ignorados:
    /// agora pertencem a cada PR do GitHub (PullRequestGithub). Devolve o registro salvo
    /// (autor/datas atualizados) e avisa em tempo real quem está com o card aberto.
    /// </summary>
    public async Task<PullRequestRegisterResponse> Create(PullRequestRegisterRequest request, CancellationToken cancellationToken)
    {
        var cardNumber = request.CardNumber?.Trim() ?? string.Empty;
        var requestExists = await _prRepository
            .FirstOrDefaultAsync(x => x.CardNumber == cardNumber, cancellationToken);

        if (requestExists is not null)
        {
            requestExists.UpdateContent(request.Description, request.RootCause);
            await CommitAsync(cancellationToken);
            await _realTimeNotifier.NotifyCardUpdatedAsync(requestExists.CardNumber, PullRequestRealTimeEvents.Actions.RegisterSaved, requestExists.Id, cancellationToken);
            return requestExists.ToResponse();
        }
        
        var requestRegister = request.Create(request);
        
        await _prRepository.AddAsync(requestRegister, cancellationToken);
        if (!await CommitAsync(cancellationToken))
            throw new Exception("Error on save");

        await _realTimeNotifier.NotifyCardUpdatedAsync(requestRegister.CardNumber, PullRequestRealTimeEvents.Actions.RegisterSaved, requestRegister.Id, cancellationToken);
        return requestRegister.ToResponse();
    }

    public async Task<PullRequestRegisterResponse> Get(int id, CancellationToken cancellationToken)
    {
        var register = await _prRepository
            .Include(x => x.GithubPullRequests)
            .FirstAsync(x => x.Id == id, cancellationToken);

        return register.ToResponse();
    }

    public async Task<PullRequestRegisterResponse?> GetByCardNumber(string cardNumber, CancellationToken cancellationToken)
    {
        var response = await _prRepository
            .AsNoTracking()
            .Include(x => x.Form)
            .Include(x => x.GithubPullRequests)
            .FirstOrDefaultAsync(x => x.CardNumber == cardNumber.Trim(), cancellationToken);

        return response?.ToResponse();
    }

    public async Task<IReadOnlyList<PullRequestRecentResponse>> GetRecentByUser(Guid userId, int take, CancellationToken cancellationToken)
    {
        if (take <= 0) take = 5;
        if (take > 50) take = 50;

        // Branch/repositório exibidos são os do PR do GitHub mais recente do card; o legado
        // da própria linha fica como fallback para cards anteriores à feature 0001.
        return await _prRepository
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Take(take)
            .Select(x => new
            {
                Register = x,
                LatestPr = x.GithubPullRequests
                    .OrderByDescending(g => g.CreatedAt)
                    .Select(g => new { g.RepositoryId, g.BranchPrefix, g.BranchName })
                    .FirstOrDefault()
            })
            .Select(x => new PullRequestRecentResponse
            {
                Id = x.Register.Id,
                CardNumber = x.Register.CardNumber,
                Description = x.Register.Description,
                RepositoryId = x.LatestPr != null ? x.LatestPr.RepositoryId : x.Register.RepositoryId,
                BranchPrefix = x.LatestPr != null ? x.LatestPr.BranchPrefix : x.Register.BranchPrefix ?? string.Empty,
                BranchName = x.LatestPr != null ? x.LatestPr.BranchName : x.Register.BranchName ?? string.Empty,
                CreatedAt = x.Register.CreatedAt,
                UpdatedAt = x.Register.UpdatedAt,
            })
            .ToListAsync(cancellationToken);
    }
}
