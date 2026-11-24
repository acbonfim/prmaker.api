using System.Diagnostics;
using solvace.governance.application;
using solvace.governance.application.Contracts;
using solvace.governance.domain;
using solvace.governance.domain.Engine;
using solvace.governance.domain.Entities;

namespace solvace.prform.Governance;

/// <summary>
/// 0098: toda gravação HTTP (POST/PUT/PATCH/DELETE em <c>api/</c>) passa pelas regras da governança antes do endpoint e
/// vira um evento da trilha depois (quem, origem, ação, card/plano/item, duração, resultado). Barrada → 403
/// <c>governance_blocked</c>; aviso → cabeçalho <c>X-Governance-Warning</c> (texto em URL-encoding). Leituras não entram.
/// O MCP tem o filtro próprio (<see cref="GovernanceMcpFilter"/>).
/// </summary>
public sealed class GovernanceMiddleware(RequestDelegate next)
{
    public const string WarningHeader = "X-Governance-Warning";

    /// <summary>Gravações de alta frequência ou da própria trilha — não viram evento nem passam pelas regras.</summary>
    private static readonly string[] Ignored =
    [
        "POST Usage/batch", "POST Governance/evaluate", "POST Governance/hooks/events", "POST Governance/rules/test",
        "POST ExecutionQueue/{id}/heartbeat", "* ExecutionQueue/next", "POST ExecutionWorker/report", "POST ExecutionWorker/doctor",
        "POST ExecutionPlan/{id}/logs", "* ExecutionPlan/{id}/session", "* ExecutionPlan/{id}/usage",
        "POST CaseChat/{card}/seen", "POST CaseChat/{card}/typing"
    ];

    public async Task InvokeAsync(HttpContext http, IGovernanceApplication governance, IGovernanceEventSink sink)
    {
        if (!IsWrite(http.Request.Method) || !http.Request.Path.StartsWithSegments("/api"))
        {
            await next(http);
            return;
        }

        var route = GovernanceRoute.Describe(http);
        if (Ignored.Any(g => GovernanceRuleEngine.Glob(g, route.Action)))
        {
            await next(http);
            return;
        }

        var caller = GovernanceCaller.From(http);
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();

        GovernanceDecision decision;
        try
        {
            decision = await governance.EvaluateAsync(new GovernanceRequest
            {
                Source = caller.Source,
                Action = route.Action,
                UserId = caller.Actor.UserId,
                UserName = caller.Actor.Name,
                Roles = caller.Actor.Roles
            }, http.RequestAborted);
        }
        catch (Exception) when (!http.RequestAborted.IsCancellationRequested)
        {
            // A governança nunca derruba a API: sem avaliação, segue (o evento sai sem regras).
            decision = GovernanceDecision.Allow(GovernanceMode.Monitor);
        }

        if (decision.Blocked)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new
            {
                code = "governance_blocked",
                message = $"Barrado pela governança: {decision.Reason}",
                rules = decision.Rules
            }, http.RequestAborted);
            Record(sink, caller, route, started, watch, decision, StatusCodes.Status403Forbidden, decision.Reason);
            return;
        }

        if (decision.Decision == GovernanceDecisionKind.Warn && decision.Reason is { } warning)
            http.Response.OnStarting(() =>
            {
                http.Response.Headers[WarningHeader] = Uri.EscapeDataString(warning);
                return Task.CompletedTask;
            });

        try
        {
            await next(http);
            Record(sink, caller, route, started, watch, decision, http.Response.StatusCode, null);
        }
        catch (Exception ex)
        {
            Record(sink, caller, route, started, watch, decision, 500, ex.GetType().Name + ": " + ex.Message);
            throw;
        }
    }

    private static bool IsWrite(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static void Record(IGovernanceEventSink sink, GovernanceCaller caller, GovernanceRoute.Info route, DateTimeOffset started,
        Stopwatch watch, GovernanceDecision decision, int status, string? detail)
    {
        var outcome = decision.Blocked ? GovernanceOutcome.Blocked
            : status >= 400 ? GovernanceOutcome.Error
            : decision.Outcome;
        sink.Enqueue(new GovernanceEventData
        {
            StartedAt = started,
            FinishedAt = started + watch.Elapsed,
            DurationMs = (int)watch.ElapsedMilliseconds,
            UserId = caller.Actor.UserId,
            UserName = caller.Actor.Name,
            Role = caller.Role,
            Source = caller.Source,
            Action = route.Action,
            Category = route.Category,
            CardNumber = route.Card,
            PlanId = route.PlanId,
            StepKey = route.StepKey,
            WorkItemNumber = route.WorkItem,
            TaskNumber = route.Task,
            Outcome = outcome,
            StatusCode = status,
            Rules = decision.Rules.Select(r => r.Key).ToList(),
            Detail = detail ?? (decision.Rules.Count > 0 ? decision.Reason ?? decision.Rules[0].Message : null),
            Machine = caller.Machine
        });
    }
}
