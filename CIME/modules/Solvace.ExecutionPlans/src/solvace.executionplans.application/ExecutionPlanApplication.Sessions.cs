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
                request.CacheReadTokens, request.CacheWriteTokens, request.Model, now, request.McpCalls, request.ScriptCalls);
            p.Touch(now, fromExecutor: true);
        }, cancellationToken);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, null, cancellationToken);
        return plan.FillSummary(new ExecutionPlanSummaryResponse(), 0, 0).Usage!;
    }

    public async Task<ExecutionPlanSummaryResponse> RequestResumeAsync(Guid planId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        // 0039: dono com executor → o pedido vai para a fila (também sem sessão registrada: começa uma nova que retoma o plano).
        if (_resumeTrigger is not null)
        {
            var current = await LoadAsync(planId, cancellationToken);
            if (!current.IsFinished && current.CreatedByUserId is { } owner
                && (await _resumeTrigger.OwnersWithWorkersAsync([owner], cancellationToken)).Count > 0)
            {
                var request = await _resumeTrigger.PlanChangedAsync(current, ExecutionRequestSource.Resume, actor, true, cancellationToken)
                              ?? throw new DomainException("O Claude já está trabalhando neste card — acompanhe por aqui.");
                return current.FillSummary(new ExecutionPlanSummaryResponse(), current.Steps.Count,
                    current.Steps.Count(s => s.Status == ExecutionStatus.Completed));
            }
            _repository.ClearTracking();
        }

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

    /// <summary>
    /// Consumo médio por plano nos últimos <paramref name="days"/> dias (0041), "com MCP" (a maioria das chamadas ao
    /// PRMake pelo MCP) × "sem MCP". Só entram planos com custo enviado pela skill.
    /// </summary>
    public async Task<ExecutionUsageReportResponse> GetUsageReportAsync(Guid? userId, int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 365);
        var plans = await _repository.GetPlansWithUsageSinceAsync(userId, DateTimeOffset.UtcNow.AddDays(-days), cancellationToken);
        var rows = plans
            .Select(p => new
            {
                p.Phase,
                Turns = p.Sessions.Sum(s => s.Turns),
                Input = p.Sessions.Sum(s => s.InputTokens + s.CacheReadTokens + s.CacheWriteTokens),
                Output = p.Sessions.Sum(s => s.OutputTokens),
                Mcp = p.Sessions.Sum(s => s.McpCalls ?? 0),
                Script = p.Sessions.Sum(s => s.ScriptCalls ?? 0)
            })
            .Where(x => x.Turns > 0)
            .Select(x => new { x.Phase, x.Turns, x.Input, x.Output, x.Mcp, x.Script, Channel = x.Mcp > 0 && x.Mcp >= x.Script ? "mcp" : "script" })
            .ToList();

        var result = new ExecutionUsageReportResponse { Days = days, AllUsers = userId is null };
        foreach (var channel in new[] { "mcp", "script" })
        foreach (var phase in new[] { "all", ExecutionPhase.Analysis, ExecutionPhase.Correction })
        {
            var group = rows.Where(r => r.Channel == channel && (phase == "all" || r.Phase == phase)).ToList();
            result.Rows.Add(new ExecutionUsageReportRow
            {
                Channel = channel,
                Phase = phase,
                Plans = group.Count,
                AvgTurns = group.Count == 0 ? 0 : Math.Round(group.Average(g => g.Turns), 1),
                AvgInputTokens = group.Count == 0 ? 0 : Math.Round(group.Average(g => (double)g.Input)),
                AvgOutputTokens = group.Count == 0 ? 0 : Math.Round(group.Average(g => (double)g.Output)),
                AvgMcpCalls = group.Count == 0 ? 0 : Math.Round(group.Average(g => g.Mcp), 1),
                AvgScriptCalls = group.Count == 0 ? 0 : Math.Round(group.Average(g => g.Script), 1)
            });
        }
        return result;
    }

    public async Task<List<ExecutionResumeCandidateResponse>> GetResumeCandidatesAsync(Guid? userId, string? host, CancellationToken cancellationToken)
    {
        var wanted = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        var rows = await _repository.GetResumeCandidatesAsync(userId, cancellationToken);
        // 0039: quem tem executor é atendido pela fila — o vigia antigo não retoma (senão rodariam dois).
        var owners = rows.Where(r => r.Plan.CreatedByUserId is not null).Select(r => r.Plan.CreatedByUserId!.Value).Distinct().ToList();
        var withWorkers = _resumeTrigger is null ? [] : await _resumeTrigger.OwnersWithWorkersAsync(owners, cancellationToken);
        return rows
            .Where(r => r.Plan.CreatedByUserId is not { } o || !withWorkers.Contains(o))
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
