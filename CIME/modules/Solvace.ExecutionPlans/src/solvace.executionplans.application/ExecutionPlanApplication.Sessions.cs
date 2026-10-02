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
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            session = p.RegisterSession(request.SessionId, request.Host, request.Cwd, now);
            await EnsureBaselineAsync(p, request.SessionId, now, cancellationToken);
            p.Touch(now, fromExecutor: true);
        }, cancellationToken);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, null, cancellationToken);
        return session!.ToResponse();
    }

    public async Task<ExecutionUsageResponse> RecordUsageAsync(Guid planId, RecordExecutionUsageRequest request, CancellationToken cancellationToken, bool sessionEnded = false)
    {
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            await EnsureBaselineAsync(p, request.SessionId, now, cancellationToken);
            p.RecordUsage(request.SessionId, request.Host, request.Turns, request.InputTokens, request.OutputTokens,
                request.CacheReadTokens, request.CacheWriteTokens, request.Model, now, request.McpCalls, request.ScriptCalls,
                request.KbCalls, request.SearchCalls, request.Models?.Select(m => new ExecutionModelUsage
                {
                    Model = m.Model, Turns = m.Turns, InputTokens = m.InputTokens, OutputTokens = m.OutputTokens,
                    CacheReadTokens = m.CacheReadTokens, CacheWriteTokens = m.CacheWriteTokens
                }).ToList());
            p.Touch(now, fromExecutor: !sessionEnded);
        }, cancellationToken);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, null, cancellationToken);
        return plan.FillSummary(new ExecutionPlanSummaryResponse(), 0, 0).Usage!;
    }

    /// <summary>
    /// 0044: correção que continua a sessão da análise — o que a sessão já registrava no plano pai vira a linha de base
    /// (o transcript é acumulado; sem isso a correção mostraria também o custo da análise).
    /// </summary>
    private async Task EnsureBaselineAsync(ExecutionPlan plan, string sessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = sessionId?.Trim();
        if (string.IsNullOrEmpty(id) || plan.Sessions.FirstOrDefault(s => s.SessionId == id) is { BaselineSet: true }) return;
        ExecutionSession? fromParent = null;
        if (plan.ParentPlanId is { } parentId
            && (await _repository.GetSessionsAsync([parentId], cancellationToken)).TryGetValue(parentId, out var sessions))
            fromParent = sessions.FirstOrDefault(s => s.SessionId == id && s.UsageUpdatedAt != null);
        plan.SetSessionBaseline(id, fromParent, now);
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

        // 0044: correção antiga (sem linha de base gravada) que continuou a sessão da análise — desconta o que a sessão
        // já tinha no plano pai, senão a correção leva também o custo da análise.
        var parentIds = plans.Where(p => p.ParentPlanId is not null && p.Sessions.Any(s => !s.BaselineSet))
            .Select(p => p.ParentPlanId!.Value).Distinct().ToList();
        var parents = await _repository.GetSessionsAsync(parentIds, cancellationToken);

        var rows = plans
            .Select(p =>
            {
                var sessions = p.Sessions.Select(s =>
                {
                    if (s.BaselineSet || p.ParentPlanId is not { } pid || !parents.TryGetValue(pid, out var ps)) return s;
                    var copy = Clone(s);
                    copy.BaselineSet = true;
                    if (ps.FirstOrDefault(x => x.SessionId == s.SessionId && x.UsageUpdatedAt != null) is { } from)
                    {
                        copy.BaseTurns = from.Turns; copy.BaseInputTokens = from.InputTokens; copy.BaseOutputTokens = from.OutputTokens;
                        copy.BaseCacheReadTokens = from.CacheReadTokens; copy.BaseCacheWriteTokens = from.CacheWriteTokens;
                        copy.BaseMcpCalls = from.McpCalls ?? 0; copy.BaseScriptCalls = from.ScriptCalls ?? 0;
                        copy.BaseKbCalls = from.KbCalls ?? 0; copy.BaseSearchCalls = from.SearchCalls ?? 0;
                        copy.BaseModels = from.Models.Select(m => m.Copy()).ToList();
                    }
                    return copy;
                }).ToList();
                return new
                {
                    p.Phase,
                    Turns = sessions.Sum(s => s.NetTurns()),
                    Fresh = sessions.Sum(s => s.NetInputTokens()),
                    CacheRead = sessions.Sum(s => s.NetCacheReadTokens()),
                    CacheWrite = sessions.Sum(s => s.NetCacheWriteTokens()),
                    Output = sessions.Sum(s => s.NetOutputTokens()),
                    Mcp = sessions.Sum(s => s.NetMcpCalls()),
                    Script = sessions.Sum(s => s.NetScriptCalls()),
                    Kb = sessions.Sum(s => s.NetKbCalls()),
                    Search = sessions.Sum(s => s.NetSearchCalls()),
                    Model = p.UsageModel,
                    // 0047: por modelo, já com a linha de base (cópia acima quando a correção é antiga).
                    Models = ExecutionPlan.NetModelUsageOf(sessions)
                };
            })
            .Where(x => x.Turns > 0)
            .Select(x => new
            {
                x.Phase, x.Turns, x.Fresh, x.CacheRead, x.CacheWrite, x.Output, x.Mcp, x.Script, x.Kb, x.Search, x.Model, x.Models,
                Input = x.Fresh + x.CacheRead + x.CacheWrite,
                Channel = x.Mcp > 0 && x.Mcp >= x.Script ? "mcp" : "script"
            })
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
                AvgFreshInputTokens = group.Count == 0 ? 0 : Math.Round(group.Average(g => (double)g.Fresh)),
                AvgCacheReadTokens = group.Count == 0 ? 0 : Math.Round(group.Average(g => (double)g.CacheRead)),
                AvgCacheWriteTokens = group.Count == 0 ? 0 : Math.Round(group.Average(g => (double)g.CacheWrite)),
                AvgTotalTokens = group.Count == 0 ? 0 : Math.Round(group.Average(g => (double)(g.Input + g.Output))),
                Model = group.Where(g => g.Model != null).GroupBy(g => g.Model).OrderByDescending(m => m.Sum(g => g.Input + g.Output))
                    .Select(m => m.Key).FirstOrDefault(),
                AvgMcpCalls = group.Count == 0 ? 0 : Math.Round(group.Average(g => g.Mcp), 1),
                AvgScriptCalls = group.Count == 0 ? 0 : Math.Round(group.Average(g => g.Script), 1),
                AvgKbCalls = group.Count == 0 ? 0 : Math.Round(group.Average(g => g.Kb), 1),
                AvgSearchCalls = group.Count == 0 ? 0 : Math.Round(group.Average(g => g.Search), 1),
                Models = group.Count == 0 ? [] : ExecutionModelUsage.Sum(group.SelectMany(g => g.Models)).Select(m => new ExecutionModelAverage
                {
                    Model = m.Model,
                    AvgTurns = Math.Round((double)m.Turns / group.Count, 1),
                    AvgInputTokens = Math.Round((double)m.InputTokens / group.Count),
                    AvgOutputTokens = Math.Round((double)m.OutputTokens / group.Count),
                    AvgCacheReadTokens = Math.Round((double)m.CacheReadTokens / group.Count),
                    AvgCacheWriteTokens = Math.Round((double)m.CacheWriteTokens / group.Count)
                }).ToList()
            });
        }
        return result;
    }

    private static ExecutionSession Clone(ExecutionSession s) => new()
    {
        SessionId = s.SessionId, Host = s.Host, Cwd = s.Cwd, StartedAt = s.StartedAt, LastSeenAt = s.LastSeenAt,
        Turns = s.Turns, InputTokens = s.InputTokens, OutputTokens = s.OutputTokens, CacheReadTokens = s.CacheReadTokens,
        CacheWriteTokens = s.CacheWriteTokens, Model = s.Model, McpCalls = s.McpCalls, ScriptCalls = s.ScriptCalls,
        KbCalls = s.KbCalls, SearchCalls = s.SearchCalls,
        UsageUpdatedAt = s.UsageUpdatedAt,
        Models = s.Models.Select(m => m.Copy()).ToList()
    };

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
