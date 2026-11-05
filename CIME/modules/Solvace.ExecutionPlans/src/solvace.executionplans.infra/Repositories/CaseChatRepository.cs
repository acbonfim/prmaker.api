using Microsoft.EntityFrameworkCore;
using Npgsql;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.infra.Contexts;

namespace solvace.executionplans.infra.Repositories;

public class CaseChatRepository(ExecutionPlanContext context) : ICaseChatRepository
{
    private static readonly HashSet<string> NumberIndexes =
        ["IX_CaseMessages_CardNumber_Number", "IX_CaseFiles_CardNumber_Number", "IX_CaseProposals_CardNumber_Number", "IX_CaseActions_CardNumber_Number", "PK_CaseChats"];

    public Task<CaseChat?> GetChatAsync(string cardNumber, CancellationToken cancellationToken) =>
        context.CaseChats.FirstOrDefaultAsync(c => c.CardNumber == cardNumber, cancellationToken);

    public void AddChat(CaseChat chat) => context.CaseChats.Add(chat);

    public async Task<int> GetMaxMessageNumberAsync(string cardNumber, CancellationToken cancellationToken) =>
        await context.CaseMessages.AsNoTracking().Where(m => m.CardNumber == cardNumber).MaxAsync(m => (int?)m.Number, cancellationToken) ?? 0;

    public void AddMessage(CaseMessage message) => context.CaseMessages.Add(message);

    public Task<CaseMessage?> GetMessageAsync(string cardNumber, Guid id, CancellationToken cancellationToken) =>
        context.CaseMessages.FirstOrDefaultAsync(m => m.CardNumber == cardNumber && m.Id == id, cancellationToken);

    public Task<CaseMessage?> GetMessageByNumberAsync(string cardNumber, int number, CancellationToken cancellationToken) =>
        context.CaseMessages.AsNoTracking().FirstOrDefaultAsync(m => m.CardNumber == cardNumber && m.Number == number && m.DeletedAt == null, cancellationToken);

    public async Task<List<CaseMessage>> GetMessagesAsync(string cardNumber, int? afterNumber, int limit, CancellationToken cancellationToken)
    {
        var query = context.CaseMessages.AsNoTracking().Where(m => m.CardNumber == cardNumber && m.DeletedAt == null);
        if (afterNumber is { } after)
            query = query.Where(m => m.Number > after);
        var last = await query.OrderByDescending(m => m.Number).Take(Math.Clamp(limit, 1, 500)).ToListAsync(cancellationToken);
        last.Reverse();
        return last;
    }

    public Task<int> DeleteMessagesUpToAsync(string cardNumber, int lastNumber, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.CaseMessages
            .Where(m => m.CardNumber == cardNumber && m.Number <= lastNumber && m.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DeletedAt, (DateTimeOffset?)now), cancellationToken);

    public Task<List<CaseMessage>> GetMessagesByRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        context.CaseMessages.Where(m => m.RequestId == requestId && m.DeletedAt == null).OrderBy(m => m.Number).ToListAsync(cancellationToken);

    public async Task<List<CaseParticipant>> GetParticipantsAsync(string cardNumber, CancellationToken cancellationToken) =>
        (await context.CaseMessages.AsNoTracking()
            .Where(m => m.CardNumber == cardNumber && m.AuthorKind == CaseAuthorKind.Person && m.DeletedAt == null)
            .GroupBy(m => new { m.AuthorUserId, m.AuthorName })
            .Select(g => new { g.Key.AuthorUserId, g.Key.AuthorName, Count = g.Count(), Last = g.Max(m => m.CreatedAt) })
            .ToListAsync(cancellationToken))
        // A mesma pessoa pode aparecer com nomes diferentes (renomeada): fica o nome mais recente.
        .GroupBy(p => p.AuthorUserId?.ToString() ?? p.AuthorName.ToLowerInvariant())
        .Select(g =>
        {
            var latest = g.OrderByDescending(x => x.Last).First();
            return new CaseParticipant(latest.AuthorUserId, latest.AuthorName, g.Sum(x => x.Count), latest.Last);
        })
        .OrderByDescending(p => p.LastAt)
        .ToList();

    public async Task<int> GetMaxFileNumberAsync(string cardNumber, CancellationToken cancellationToken) =>
        await context.CaseFiles.AsNoTracking().Where(f => f.CardNumber == cardNumber).MaxAsync(f => (int?)f.Number, cancellationToken) ?? 0;

    public Task<long> GetFilesSizeAsync(string cardNumber, CancellationToken cancellationToken) =>
        context.CaseFiles.AsNoTracking().Where(f => f.CardNumber == cardNumber).SumAsync(f => f.Size, cancellationToken);

    public void AddFile(CaseFile file) => context.CaseFiles.Add(file);

    public Task<List<CaseFile>> GetFilesAsync(string cardNumber, IReadOnlyCollection<Guid>? messageIds, CancellationToken cancellationToken)
    {
        var query = context.CaseFiles.AsNoTracking().Where(f => f.CardNumber == cardNumber);
        if (messageIds is not null)
            query = query.Where(f => f.MessageId != null && messageIds.Contains(f.MessageId.Value));
        return query.OrderBy(f => f.Number).ToListAsync(cancellationToken);
    }

    public Task<CaseFile?> GetFileAsync(string cardNumber, Guid id, CancellationToken cancellationToken) =>
        context.CaseFiles.AsNoTracking().FirstOrDefaultAsync(f => f.CardNumber == cardNumber && f.Id == id, cancellationToken);

    public Task<CaseFile?> GetFileByNumberAsync(string cardNumber, int number, CancellationToken cancellationToken) =>
        context.CaseFiles.AsNoTracking().FirstOrDefaultAsync(f => f.CardNumber == cardNumber && f.Number == number, cancellationToken);

    public Task<byte[]?> GetFileContentAsync(Guid fileId, CancellationToken cancellationToken) =>
        context.CaseFileContents.AsNoTracking().Where(c => c.FileId == fileId).Select(c => c.Data).FirstOrDefaultAsync(cancellationToken);

    public Task AttachFilesAsync(IReadOnlyCollection<Guid> fileIds, Guid messageId, CancellationToken cancellationToken) =>
        context.CaseFiles.Where(f => fileIds.Contains(f.Id) && (f.MessageId == null || f.MessageId == messageId))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.MessageId, (Guid?)messageId), cancellationToken);

    public Task<List<CasePoint>> GetPointsAsync(string cardNumber, CancellationToken cancellationToken) =>
        context.CasePoints.AsNoTracking().Where(p => p.CardNumber == cardNumber).OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);

    public Task<CasePoint?> GetPointAsync(string cardNumber, Guid id, CancellationToken cancellationToken) =>
        context.CasePoints.FirstOrDefaultAsync(p => p.CardNumber == cardNumber && p.Id == id, cancellationToken);

    public void AddPoint(CasePoint point) => context.CasePoints.Add(point);

    public async Task<int> GetMaxProposalNumberAsync(string cardNumber, CancellationToken cancellationToken) =>
        await context.CaseProposals.AsNoTracking().Where(p => p.CardNumber == cardNumber).MaxAsync(p => (int?)p.Number, cancellationToken) ?? 0;

    public Task<List<CaseProposal>> GetProposalsAsync(string cardNumber, CancellationToken cancellationToken) =>
        context.CaseProposals.Where(p => p.CardNumber == cardNumber).OrderBy(p => p.Number).ToListAsync(cancellationToken);

    public Task<CaseProposal?> GetProposalAsync(string cardNumber, Guid id, CancellationToken cancellationToken) =>
        context.CaseProposals.FirstOrDefaultAsync(p => p.CardNumber == cardNumber && p.Id == id, cancellationToken);

    public void AddProposal(CaseProposal proposal) => context.CaseProposals.Add(proposal);

    public async Task<int> GetMaxActionNumberAsync(string cardNumber, CancellationToken cancellationToken) =>
        await context.CaseActions.AsNoTracking().Where(a => a.CardNumber == cardNumber).MaxAsync(a => (int?)a.Number, cancellationToken) ?? 0;

    public Task<List<CaseAction>> GetActionsAsync(string cardNumber, CancellationToken cancellationToken) =>
        context.CaseActions.Where(a => a.CardNumber == cardNumber).OrderBy(a => a.Number).ToListAsync(cancellationToken);

    public Task<CaseAction?> GetActionAsync(string cardNumber, Guid id, CancellationToken cancellationToken) =>
        context.CaseActions.FirstOrDefaultAsync(a => a.CardNumber == cardNumber && a.Id == id, cancellationToken);

    public void AddAction(CaseAction action) => context.CaseActions.Add(action);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ExecutionPlanConcurrencyException(e);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                          && pg.ConstraintName is { } name && NumberIndexes.Contains(name))
        {
            throw new CaseNumberConflictException(e);
        }
    }

    public void ClearTracking() => context.ChangeTracker.Clear();
}
