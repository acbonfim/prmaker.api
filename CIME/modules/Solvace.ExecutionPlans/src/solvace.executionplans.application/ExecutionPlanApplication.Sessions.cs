using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// Retomar sem copiar comando (0033): a skill registra a sessão do Claude Code (id, máquina, pasta) e o custo dela;
/// a tela pede "continuar" e o vigia local da máquina retoma aquela sessão (<c>claude --resume</c>).
/// </summary>
public partial class ExecutionPlanApplication
{
    public async Task<ExecutionSessionResponse> RegisterSessionAsync(Guid planId, RegisterExecutionSessionRequest request, CancellationToken cancellationToken)
    {
        ExecutionSession? session = null;
        var plan = await MutateAsync(planId, p =>
        {
            var now = DateTimeOffset.UtcNow;
            session = p.RegisterSession(request.SessionId, request.Host, request.Cwd, now);
            p.Touch(now, fromExecutor: true);
        }, cancellationToken);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, null, cancellationToken);
        return session!.ToResponse();
    }

    public async Task<ExecutionUsageResponse> RecordUsageAsync(Guid planId, RecordExecutionUsageRequest request, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, p =>
        {
            var now = DateTimeOffset.UtcNow;
            p.RecordUsage(request.SessionId, request.Host, request.Turns, request.InputTokens, request.OutputTokens,
                request.CacheReadTokens, request.CacheWriteTokens, request.Model, now);
            p.Touch(now, fromExecutor: true);
        }, cancellationToken);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, null, cancellationToken);
        return plan.FillSummary(new ExecutionPlanSummaryResponse(), 0, 0).Usage!;
    }

    public async Task<ExecutionPlanSummaryResponse> RequestResumeAsync(Guid planId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, p => p.RequestResume(actor.Name, DateTimeOffset.UtcNow), cancellationToken, actor);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, null, cancellationToken);
        return plan.FillSummary(new ExecutionPlanSummaryResponse(), plan.Steps.Count, plan.Steps.Count(s => s.Status == ExecutionStatus.Completed));
    }

    public async Task AcknowledgeResumeAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, p =>
        {
            var now = DateTimeOffset.UtcNow;
            p.AcknowledgeResume(now);
            p.Touch(now, fromExecutor: true);
        }, cancellationToken);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, null, cancellationToken);
    }

    public async Task<List<ExecutionResumeCandidateResponse>> GetResumeCandidatesAsync(Guid? userId, string? host, CancellationToken cancellationToken)
    {
        var wanted = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        return (await _repository.GetResumeCandidatesAsync(userId, cancellationToken))
            .Where(r => r.Plan.CurrentSession is { } s && (wanted is null || string.Equals(s.Host, wanted, StringComparison.OrdinalIgnoreCase)))
            .Select(r =>
            {
                var byRequest = r.Plan.ResumePending;
                return new ExecutionResumeCandidateResponse
                {
                    PlanId = r.Plan.Id,
                    CardNumber = r.Plan.CardNumber,
                    Title = r.Plan.Title,
                    Phase = r.Plan.Phase,
                    Status = r.Plan.Status,
                    Session = r.Plan.CurrentSession!.ToResponse(),
                    Reason = byRequest ? "resume-request" : "answers",
                    RequestedBy = byRequest ? r.Plan.ResumeRequestedBy : null,
                    Since = byRequest ? r.Plan.ResumeRequestedAt!.Value : r.AnsweredAt!.Value
                };
            })
            .OrderBy(c => c.Since)
            .ToList();
    }
}
