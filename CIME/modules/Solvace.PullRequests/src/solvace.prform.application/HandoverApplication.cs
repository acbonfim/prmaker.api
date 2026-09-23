using Microsoft.EntityFrameworkCore;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.application;

public class HandoverApplication : IHandoverApplication
{
    private readonly DefaultContext _context;
    private readonly DbSet<HandoverRegister> _repository;

    public HandoverApplication(DefaultContext context)
    {
        _context = context;
        _repository = context.Handovers;
    }

    public async Task<bool> CommitAsync(CancellationToken cancellationToken)
    {
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<HandoverResponse?> GetByCardNumber(string cardNumber, CancellationToken cancellationToken)
    {
        var handover = await _repository
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CardNumber == cardNumber, cancellationToken);

        return handover?.ToResponse();
    }

    public async Task<IReadOnlyList<HandoverRecentResponse>> GetRecent(int take, CancellationToken cancellationToken)
    {
        if (take <= 0) take = 10;
        if (take > 50) take = 50;

        return await _repository
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new HandoverRecentResponse
            {
                Id = x.Id,
                CardNumber = x.CardNumber,
                RepositoryId = x.RepositoryId,
                UserId = x.CreatedBy,
                CreatedAt = x.CreatedAt,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<HandoverResponse> Save(HandoverRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CardNumber))
            throw new ArgumentException("CardNumber é obrigatório", nameof(request));

        var existing = await _repository
            .FirstOrDefaultAsync(x => x.CardNumber == request.CardNumber, cancellationToken);

        if (existing is not null)
        {
            // Regeneração preserva a visibilidade atual; o flag só muda pelo endpoint dedicado.
            existing.UpdateContent(request.Content, request.RepositoryId);
            _repository.Update(existing);
            await CommitAsync(cancellationToken);
            return existing.ToResponse();
        }

        var handover = new HandoverRegister(request.CardNumber, request.Content, request.RepositoryId, request.IsPublic);
        // CreatedBy é preenchido automaticamente pelo AuditSaveChangesInterceptor (externalId do token).
        var created = await _repository.AddAsync(handover, cancellationToken);
        await CommitAsync(cancellationToken);
        return created.Entity.ToResponse();
    }

    public async Task<HandoverResponse?> SetVisibility(string cardNumber, bool isPublic, CancellationToken cancellationToken)
    {
        var existing = await _repository
            .FirstOrDefaultAsync(x => x.CardNumber == cardNumber, cancellationToken);

        if (existing is null)
            return null;

        existing.SetVisibility(isPublic);
        _repository.Update(existing);
        await CommitAsync(cancellationToken);
        return existing.ToResponse();
    }
}
