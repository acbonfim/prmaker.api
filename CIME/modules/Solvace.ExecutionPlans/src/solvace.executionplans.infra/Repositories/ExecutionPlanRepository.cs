using Microsoft.EntityFrameworkCore;
using Npgsql;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.executionplans.infra.Contexts;

namespace solvace.executionplans.infra.Repositories;

public class ExecutionPlanRepository : IExecutionPlanRepository
{
    private readonly ExecutionPlanContext _context;

    public ExecutionPlanRepository(ExecutionPlanContext context)
    {
        _context = context;
    }

    public void AddPlan(ExecutionPlan plan) => _context.Plans.Add(plan);

    public Task<ExecutionPlan?> GetPlanWithStepsAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Plans
            .Include(p => p.Steps)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<List<ExecutionPlanSummaryResponse>> GetSummariesByCardAsync(string cardNumber, CancellationToken cancellationToken)
    {
        // Poucos planos por card: carrega as etapas para calcular as pendências do usuário com a mesma regra da tela (0037).
        var plans = await _context.Plans
            .AsNoTracking()
            .Include(p => p.Steps)
            .Where(p => p.CardNumber == cardNumber)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
        var questions = await GetOpenQuestionsAsync(plans.Select(p => p.Id).ToList(), cancellationToken);

        var summaries = plans.Select(p =>
        {
            var summary = p.FillSummary(new ExecutionPlanSummaryResponse(), p.Steps.Count, p.Steps.Count(s => s.Status == ExecutionStatus.Completed));
            summary.UserPending = p.UserActions(questions.Where(q => q.PlanId == p.Id)).Count;
            return summary;
        }).ToList();
        ExecutionPlanMappings.AssignVersionNumbers(summaries);
        return summaries;
    }

    public Task<List<ExecutionPlan>> GetActivePlansByUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var active = new[] { ExecutionStatus.Pending, ExecutionStatus.Running, ExecutionStatus.Paused };
        return _context.Plans
            .AsNoTracking()
            .Include(p => p.Steps)
            .Where(p => p.CreatedByUserId == userId && active.Contains(p.Status))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<List<ExecutionQuestion>> GetOpenQuestionsAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken) =>
        planIds.Count == 0
            ? Task.FromResult(new List<ExecutionQuestion>())
            : _context.Questions
                .AsNoTracking()
                .Where(q => planIds.Contains(q.PlanId) && q.Status == ExecutionQuestionStatus.Open)
                .ToListAsync(cancellationToken);

    public async Task<List<(ExecutionPlan Plan, DateTimeOffset? AnsweredAt)>> GetResumeCandidatesAsync(Guid? userId, CancellationToken cancellationToken)
    {
        var active = new[] { ExecutionStatus.Pending, ExecutionStatus.Running, ExecutionStatus.Paused };
        var rows = await _context.Plans
            .AsNoTracking()
            .Where(p => active.Contains(p.Status) && (userId == null || p.CreatedByUserId == userId))
            .Select(p => new
            {
                Plan = p,
                AnsweredAt = _context.Questions
                    .Where(q => q.PlanId == p.Id && q.Status == ExecutionQuestionStatus.Answered && q.AnsweredVia == "prmake"
                                && (p.LastActivityAt == null || q.AnsweredAt > p.LastActivityAt))
                    .Max(q => q.AnsweredAt)
            })
            .Where(r => r.AnsweredAt != null || (r.Plan.ResumeRequestedAt != null
                        && (r.Plan.ResumeHandledAt == null || r.Plan.ResumeHandledAt < r.Plan.ResumeRequestedAt)))
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.Plan, r.AnsweredAt)).ToList();
    }

    public async Task<List<ExecutionPlan>> GetPlansWithUsageSinceAsync(Guid? userId, DateTimeOffset since, CancellationToken cancellationToken) =>
        (await _context.Plans.AsNoTracking()
            .Where(p => p.UpdatedAt >= since && (userId == null || p.CreatedByUserId == userId))
            .OrderByDescending(p => p.UpdatedAt)
            .Take(5000)
            .ToListAsync(cancellationToken))
        .Where(p => p.Sessions.Any(s => s.UsageUpdatedAt != null))
        .ToList();

    public async Task<Dictionary<Guid, List<ExecutionSession>>> GetSessionsAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken)
    {
        if (planIds.Count == 0) return [];
        var plans = await _context.Plans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToListAsync(cancellationToken);
        return plans.ToDictionary(p => p.Id, p => p.Sessions.ToList());
    }

    public Task<Guid?> GetCurrentPlanIdAsync(string cardNumber, CancellationToken cancellationToken) =>
        _context.Plans
            .AsNoTracking()
            .Where(p => p.CardNumber == cardNumber)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<long> GetLastLogIdAsync(Guid planId, CancellationToken cancellationToken) =>
        await _context.Logs
            .Where(l => l.PlanId == planId)
            .MaxAsync(l => (long?)l.Id, cancellationToken) ?? 0;

    public async Task<HashSet<string>> GetExistingClientIdsAsync(Guid planId, IReadOnlyCollection<string> clientIds, CancellationToken cancellationToken)
    {
        var existing = await _context.Logs
            .AsNoTracking()
            .Where(l => l.PlanId == planId && l.ClientId != null && clientIds.Contains(l.ClientId))
            .Select(l => l.ClientId!)
            .ToListAsync(cancellationToken);
        return existing.ToHashSet();
    }

    public void AddLogs(IEnumerable<ExecutionLog> logs) => _context.Logs.AddRange(logs);

    public Task<List<ExecutionLog>> GetLogsAsync(Guid planId, long afterId, string? stepKey, int limit, CancellationToken cancellationToken)
    {
        var query = _context.Logs.AsNoTracking().Where(l => l.PlanId == planId && l.Id > afterId);
        if (stepKey is not null)
            query = query.Where(l => l.StepKey == stepKey);
        return query.OrderBy(l => l.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public Task<List<ExecutionArtifact>> GetArtifactsAsync(Guid planId, CancellationToken cancellationToken) =>
        _context.Artifacts
            .AsNoTracking()
            .Where(a => a.PlanId == planId)
            .OrderBy(a => a.Kind).ThenBy(a => a.Name)
            .ToListAsync(cancellationToken);

    public Task<ExecutionArtifact?> GetArtifactAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken) =>
        _context.Artifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.PlanId == planId && a.Id == artifactId, cancellationToken);

    public Task<ExecutionArtifact?> FindArtifactAsync(Guid planId, string kind, string name, CancellationToken cancellationToken) =>
        _context.Artifacts
            .Include(a => a.Content)
            .FirstOrDefaultAsync(a => a.PlanId == planId && a.Kind == kind && a.Name == name, cancellationToken);

    public Task<byte[]?> GetArtifactContentAsync(Guid artifactId, CancellationToken cancellationToken) =>
        _context.ArtifactContents
            .AsNoTracking()
            .Where(c => c.ArtifactId == artifactId)
            .Select(c => c.Data)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<long> GetArtifactsSizeAsync(Guid planId, Guid? excludingArtifactId, CancellationToken cancellationToken) =>
        await _context.Artifacts
            .Where(a => a.PlanId == planId && (excludingArtifactId == null || a.Id != excludingArtifactId))
            .SumAsync(a => (long?)a.Size, cancellationToken) ?? 0;

    public void AddArtifact(ExecutionArtifact artifact) => _context.Artifacts.Add(artifact);

    public async Task RemoveArtifactAsync(Guid artifactId, CancellationToken cancellationToken)
    {
        var artifact = await _context.Artifacts
            .Include(a => a.Content)
            .FirstOrDefaultAsync(a => a.Id == artifactId, cancellationToken);
        if (artifact is not null)
            _context.Artifacts.Remove(artifact);
    }

    public void AddQuestions(IEnumerable<ExecutionQuestion> questions) => _context.Questions.AddRange(questions);

    public Task<List<ExecutionQuestion>> GetQuestionsAsync(Guid planId, CancellationToken cancellationToken) =>
        _context.Questions.Where(q => q.PlanId == planId).OrderBy(q => q.CreatedAt).ThenBy(q => q.Order).ToListAsync(cancellationToken);

    public Task<ExecutionQuestion?> GetQuestionAsync(Guid planId, Guid questionId, CancellationToken cancellationToken) =>
        _context.Questions.FirstOrDefaultAsync(q => q.PlanId == planId && q.Id == questionId, cancellationToken);

    public void AddLink(ExecutionLink link) => _context.Links.Add(link);

    public void RemoveLink(ExecutionLink link) => _context.Links.Remove(link);

    public Task<List<ExecutionLink>> GetLinksAsync(Guid planId, CancellationToken cancellationToken) =>
        _context.Links.Where(l => l.PlanId == planId).OrderByDescending(l => l.CreatedAt).ToListAsync(cancellationToken);

    public Task<List<ExecutionLink>> GetStepLinksAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken) =>
        planIds.Count == 0
            ? Task.FromResult(new List<ExecutionLink>())
            : _context.Links.AsNoTracking()
                .Where(l => planIds.Contains(l.PlanId) && (l.Kind == ExecutionLinkKind.Ticket || l.Kind == ExecutionLinkKind.PullRequest))
                .OrderBy(l => l.CreatedAt).ToListAsync(cancellationToken);

    public Task<ExecutionLink?> GetLinkAsync(Guid planId, Guid linkId, CancellationToken cancellationToken) =>
        _context.Links.FirstOrDefaultAsync(l => l.PlanId == planId && l.Id == linkId, cancellationToken);

    // Comentários e numeração por card (0031)

    public void AddNote(ExecutionNote note) => _context.Notes.Add(note);

    public Task<ExecutionNote?> GetNoteAsync(Guid planId, Guid noteId, CancellationToken cancellationToken) =>
        _context.Notes.FirstOrDefaultAsync(n => n.PlanId == planId && n.Id == noteId && n.DeletedAt == null, cancellationToken);

    public Task<List<ExecutionNote>> GetNotesByCardAsync(string cardNumber, CancellationToken cancellationToken) =>
        _context.Notes.AsNoTracking()
            .Where(n => n.CardNumber == cardNumber && n.DeletedAt == null)
            .OrderBy(n => n.Number).ThenBy(n => n.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> MarkNotesReadAsync(string cardNumber, int number, DateTimeOffset now, CancellationToken cancellationToken) =>
        await _context.Plans
            .Where(p => p.CardNumber == cardNumber && (p.NotesReadNumber == null || p.NotesReadNumber < number))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.NotesReadNumber, number).SetProperty(p => p.NotesReadAt, now), cancellationToken) > 0;

    public Task<List<ExecutionArtifact>> GetNoteAttachmentsAsync(IReadOnlyCollection<Guid> noteIds, CancellationToken cancellationToken) =>
        noteIds.Count == 0
            ? Task.FromResult(new List<ExecutionArtifact>())
            : _context.Artifacts.AsNoTracking().Where(a => a.NoteId != null && noteIds.Contains(a.NoteId.Value)).ToListAsync(cancellationToken);

    public Task<ExecutionArtifact?> FindNoteAttachmentByShaAsync(string cardNumber, string sha256, CancellationToken cancellationToken) =>
        (from a in _context.Artifacts.AsNoTracking()
         join p in _context.Plans on a.PlanId equals p.Id
         where p.CardNumber == cardNumber && a.NoteId != null && a.Sha256 == sha256
         orderby a.Number
         select a).FirstOrDefaultAsync(cancellationToken);

    public async Task<int> GetMaxNoteNumberAsync(string cardNumber, CancellationToken cancellationToken) =>
        await _context.Notes.Where(n => n.CardNumber == cardNumber).MaxAsync(n => (int?)n.Number, cancellationToken) ?? 0;

    public async Task<int> GetMaxArtifactNumberAsync(string cardNumber, CancellationToken cancellationToken) =>
        await (from a in _context.Artifacts
               join p in _context.Plans on a.PlanId equals p.Id
               where p.CardNumber == cardNumber
               select (int?)a.Number).MaxAsync(cancellationToken) ?? 0;

    public async Task<(int LastNumber, DateTimeOffset? ChangedAt)> GetUserNotesStateAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var notes = await _context.Notes.AsNoTracking()
            .Where(n => n.CardNumber == cardNumber && !n.FromExecutor)
            .Select(n => new { n.Number, n.CreatedAt, n.UpdatedAt, n.DeletedAt })
            .ToListAsync(cancellationToken);
        if (notes.Count == 0) return (0, null);
        var last = notes.Where(n => n.DeletedAt == null).Select(n => n.Number).DefaultIfEmpty(0).Max();
        var changed = notes.Select(n => new[] { n.CreatedAt, n.UpdatedAt ?? n.CreatedAt, n.DeletedAt ?? n.CreatedAt }.Max()).Max();
        return (last, changed);
    }

    public Task<List<ExecutionArtifact>> GetCardArtifactsAsync(string cardNumber, CancellationToken cancellationToken) =>
        (from a in _context.Artifacts.AsNoTracking()
         join p in _context.Plans on a.PlanId equals p.Id
         where p.CardNumber == cardNumber
         orderby a.Number
         select a).ToListAsync(cancellationToken);

    public Task<int?> GetSituationCommentIdAsync(Guid planId, CancellationToken cancellationToken) =>
        _context.Plans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.SituationCommentId).FirstOrDefaultAsync(cancellationToken);

    public async Task SetSituationPostedAsync(Guid planId, int commentId, string markdown, string by, DateTimeOffset at, CancellationToken cancellationToken) =>
        await _context.Plans.Where(p => p.Id == planId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SituationCommentId, commentId)
                .SetProperty(p => p.SituationPostedText, markdown)
                .SetProperty(p => p.SituationPostedBy, by.Length <= 200 ? by : by.Substring(0, 200))
                .SetProperty(p => p.SituationPostedAt, at), cancellationToken);

    public Task<bool?> GetSituationAutoPostAsync(Guid userId, CancellationToken cancellationToken) =>
        _context.UserSettings.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.SituationAutoPost).FirstOrDefaultAsync(cancellationToken);

    public async Task SetSituationAutoPostAsync(Guid userId, bool? value, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = await _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (settings is null)
        {
            settings = new ExecutionUserSettings(userId, now);
            _context.UserSettings.Add(settings);
        }
        settings.SetSituationAutoPost(value, now);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<string?> GetSituationKindAsync(Guid planId, CancellationToken cancellationToken) =>
        _context.Plans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.SituationKind).FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> SetSituationKindAsync(Guid planId, string? expected, string kind, DateTimeOffset at, CancellationToken cancellationToken) =>
        await _context.Plans.Where(p => p.Id == planId && p.SituationKind == expected)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SituationKind, kind).SetProperty(p => p.SituationAt, at), cancellationToken) > 0;

    public async Task MarkArtifactReadAsync(Guid artifactId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await _context.Artifacts.Where(a => a.Id == artifactId && a.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReadAt, now), cancellationToken);

    public Task<ExecutionArtifact?> FindArtifactWithContentAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken) =>
        _context.Artifacts.Include(a => a.Content).FirstOrDefaultAsync(a => a.PlanId == planId && a.Id == artifactId, cancellationToken);

    // Assistente do card (0078)

    public void AddSuggestion(ExecutionSuggestion suggestion) => _context.Suggestions.Add(suggestion);

    public Task<bool> SuggestionExistsAsync(string cardNumber, string dedupKey, CancellationToken cancellationToken) =>
        _context.Suggestions.AnyAsync(s => s.CardNumber == cardNumber && s.DedupKey == dedupKey, cancellationToken);

    public Task<ExecutionSuggestion?> FindSuggestionAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Suggestions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<List<ExecutionSuggestion>> GetSuggestionsAsync(string cardNumber, CancellationToken cancellationToken) =>
        _context.Suggestions.AsNoTracking().Where(s => s.CardNumber == cardNumber)
            .OrderByDescending(s => s.CreatedAt).Take(60).ToListAsync(cancellationToken);

    // Fotografias do código citado (0075)

    public Task<ExecutionCodeRef?> FindCodeRefAsync(Guid planId, string reference, CancellationToken cancellationToken) =>
        _context.CodeRefs.FirstOrDefaultAsync(r => r.PlanId == planId && r.Ref == reference, cancellationToken);

    public Task<int> CountCodeRefsAsync(Guid planId, CancellationToken cancellationToken) =>
        _context.CodeRefs.CountAsync(r => r.PlanId == planId, cancellationToken);

    public void AddCodeRef(ExecutionCodeRef codeRef) => _context.CodeRefs.Add(codeRef);

    public void RemoveCodeRef(ExecutionCodeRef codeRef) => _context.CodeRefs.Remove(codeRef);

    public Task<List<ExecutionCodeRefHeadResponse>> GetCodeRefHeadsAsync(string cardNumber, Guid? planId, CancellationToken cancellationToken) =>
        _context.CodeRefs.AsNoTracking()
            .Where(r => r.CardNumber == cardNumber && (planId == null || r.PlanId == planId))
            .OrderBy(r => r.Ref)
            .Select(r => new ExecutionCodeRefHeadResponse
            {
                Id = r.Id,
                PlanId = r.PlanId,
                Ref = r.Ref,
                Repository = r.Repository,
                Path = r.Path,
                StartLine = r.StartLine,
                EndLine = r.EndLine,
                Change = r.Change,
                Title = r.Title,
                CommitSha = r.CommitSha,
                HasProposal = r.Proposal != null,
                HasImplemented = r.Implemented != null,
                UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
            })
            .ToListAsync(cancellationToken);

    public Task<ExecutionCodeRef?> GetCodeRefAsync(Guid id, CancellationToken cancellationToken) =>
        _context.CodeRefs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<List<(Guid Id, string Status)>> GetCardPlanStatusesAsync(string cardNumber, CancellationToken cancellationToken) =>
        (await _context.Plans.AsNoTracking()
            .Where(p => p.CardNumber == cardNumber)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Status })
            .ToListAsync(cancellationToken))
        .Select(p => (p.Id, p.Status)).ToList();

    public Task<ExecutionCodeRef?> FindCardCodeRefAsync(string cardNumber, string reference, CancellationToken cancellationToken) =>
        _context.CodeRefs.FirstOrDefaultAsync(r => r.PlanId == null && r.CardNumber == cardNumber && r.Ref == reference, cancellationToken);

    public Task<int> CountCardCodeRefsAsync(string cardNumber, CancellationToken cancellationToken) =>
        _context.CodeRefs.CountAsync(r => r.PlanId == null && r.CardNumber == cardNumber, cancellationToken);

    public Task<List<WorkItemFile>> GetCardWorkItemFilesByNameAsync(string cardNumber, string name, CancellationToken cancellationToken)
    {
        var wanted = name.ToLower();
        // Sem o conteúdo (Content não é carregado): só quem for escolhido tem os bytes lidos.
        return (from f in _context.WorkItemFiles.AsNoTracking()
                join i in _context.WorkItems.AsNoTracking() on f.WorkItemId equals i.Id
                where i.CardNumber == cardNumber && f.Name.ToLower() == wanted
                select f).ToListAsync(cancellationToken);
    }

    public Task<byte[]?> GetWorkItemFileContentAsync(Guid fileId, CancellationToken cancellationToken) =>
        _context.WorkItemFileContents.AsNoTracking().Where(c => c.FileId == fileId).Select(c => c.Data).FirstOrDefaultAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ExecutionPlanConcurrencyException(e);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                          && pg.ConstraintName == ExecutionPlanContext.CardCodeRefIndex)
        {
            // Mesma fotografia do card sem plano gravada ao mesmo tempo: quem chama relê e tenta de novo.
            throw new ExecutionPlanConcurrencyException(e);
        }
    }

    public void ClearTracking() => _context.ChangeTracker.Clear();
}
