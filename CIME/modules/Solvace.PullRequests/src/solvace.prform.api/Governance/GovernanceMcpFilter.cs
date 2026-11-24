using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using solvace.governance.application;
using solvace.governance.application.Contracts;
using solvace.governance.domain;
using solvace.governance.domain.Engine;
using solvace.governance.domain.Entities;
using solvace.governance.domain.Standards;

namespace solvace.prform.Governance;

/// <summary>
/// 0098: toda chamada de ferramenta do MCP do PRMake passa pelas regras (ação <c>mcp:&lt;ferramenta&gt;</c>, entrada =
/// argumentos resumidos) e vira evento da trilha com card, fase, item e sub-tarefa dos argumentos. Barrada → resultado
/// de erro com o motivo (o Claude lê e segue com o resto); aviso → texto acrescentado ao resultado.
/// </summary>
public static class GovernanceMcpFilter
{
    /// <summary>Ferramentas de leitura muito chamadas: passam pelas regras, mas não viram evento (custo da trilha).</summary>
    private static readonly string[] ReadOnly =
    [
        "prmake_plan", "prmake_plan_status", "prmake_card", "prmake_config", "prmake_rules", "prmake_steps", "prmake_answers",
        "prmake_base_*", "prmake_work_items", "prmake_work_item", "prmake_case_context", "prmake_case_messages", "prmake_notes",
        "prmake_attachment", "prmake_evidence", "prmake_devops_config", "prmake_log"
    ];

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, ct) =>
        {
            var http = context.Services?.GetService<IHttpContextAccessor>()?.HttpContext;
            if (http is null) return await next(context, ct);

            var services = http.RequestServices;
            var governance = services.GetRequiredService<IGovernanceApplication>();
            var sink = services.GetRequiredService<IGovernanceEventSink>();
            var tool = context.Params?.Name ?? "?";
            var caller = GovernanceCaller.From(http, forcedSource: GovernanceSource.Mcp);
            var input = Summary(context.Params);
            var started = DateTimeOffset.UtcNow;
            var watch = Stopwatch.StartNew();

            GovernanceDecision decision;
            try
            {
                decision = await governance.EvaluateAsync(new GovernanceRequest
                {
                    Source = caller.Source,
                    Action = "mcp:" + tool,
                    Tool = "mcp__prmake__" + tool,
                    Input = input,
                    UserId = caller.Actor.UserId,
                    UserName = caller.Actor.Name,
                    Roles = caller.Actor.Roles
                }, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                decision = GovernanceDecision.Allow(GovernanceMode.Monitor);
            }

            var record = decision.Rules.Count > 0 || !ReadOnly.Any(g => GovernanceRuleEngine.Glob(g, tool));
            if (decision.Blocked)
            {
                if (record) Record(sink, caller, tool, context.Params, started, watch, decision, GovernanceOutcome.Blocked, decision.Reason);
                var rules = string.Join(", ", decision.Rules.Where(r => GovernanceEffect.Weight(r.Effect) == 3).Select(r => r.Key));
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = $"Barrado pela governança do PRMake: {decision.Reason} (regra {rules}). Não contorne; registre no plano o que precisa de uma pessoa e siga com o resto." }]
                };
            }

            // 0101: script SQL/migração gravado pelo PRMake passa pelos padrões antes de gravar.
            var standardsNote = (string?)null;
            if (StandardsTarget(tool, context.Params) is { } std)
            {
                StandardCheckResult? check = null;
                try
                {
                    check = await services.GetRequiredService<IGovernanceStandardsApplication>().CheckAsync(new StandardCheckInput
                    {
                        Text = std.Text, Target = std.Target, Path = std.Name, Card = Int(context.Params, "card"),
                        Source = caller.Source is GovernanceSource.Executor or GovernanceSource.Specialist ? caller.Source : GovernanceSource.Skill
                    }, caller.Actor, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Padrões indisponíveis: grava (a governança nunca derruba o fluxo).
                }
                if (check is { Decision: GovernanceDecisionKind.Block })
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = $"Arquivo {std.Name} não gravado — {solvace.prform.Execution.StandardsMcpTools.Format(check)}" }]
                    };
                if (check is { Findings.Count: > 0 })
                    standardsNote = $"Padrões ({std.Name}): {solvace.prform.Execution.StandardsMcpTools.Format(check)}";
            }

            try
            {
                var result = await next(context, ct);
                if (standardsNote is not null && result.IsError != true)
                    result.Content = [.. result.Content, new TextContentBlock { Text = standardsNote }];
                if (decision.Decision == GovernanceDecisionKind.Warn && decision.Reason is { } warning)
                    result.Content = [.. result.Content, new TextContentBlock { Text = $"Aviso da governança do PRMake: {warning}" }];
                if (record)
                    Record(sink, caller, tool, context.Params, started, watch, decision,
                        result.IsError == true ? GovernanceOutcome.Error : decision.Outcome, result.IsError == true ? Text(result) : null);
                return result;
            }
            catch (Exception e)
            {
                Record(sink, caller, tool, context.Params, started, watch, decision, GovernanceOutcome.Error, e.GetType().Name + ": " + e.Message);
                throw;
            }
        };

    private static void Record(IGovernanceEventSink sink, GovernanceCaller caller, string tool, CallToolRequestParams? p, DateTimeOffset started,
        Stopwatch watch, GovernanceDecision decision, string outcome, string? detail) =>
        sink.Enqueue(new GovernanceEventData
        {
            StartedAt = started,
            FinishedAt = started + watch.Elapsed,
            DurationMs = (int)watch.ElapsedMilliseconds,
            UserId = caller.Actor.UserId,
            UserName = caller.Actor.Name,
            Role = caller.Role,
            Source = caller.Source,
            Action = "mcp:" + tool,
            Category = Category(tool),
            CardNumber = Int(p, "card"),
            Phase = Str(p, "phase"),
            StepKey = Str(p, "key") ?? Str(p, "step"),
            WorkItemNumber = Int(p, "number") ?? Int(p, "item"),
            TaskNumber = Int(p, "task") ?? Int(p, "taskNumber"),
            Outcome = outcome,
            Rules = decision.Rules.Select(r => r.Key).ToList(),
            Detail = detail ?? (decision.Rules.Count > 0 ? decision.Reason ?? decision.Rules[0].Message : Summary(p)),
            Machine = caller.Machine
        });

    /// <summary>0101: gravação de arquivo (plano/item) que é script SQL ou migração → (nome, conteúdo, alvo da conferência).</summary>
    public static (string Name, string Text, string Target)? StandardsTarget(string tool, CallToolRequestParams? p)
    {
        if (tool is not ("prmake_file" or "prmake_work_item_file")) return null;
        var name = Str(p, "name");
        var content = Str(p, "content");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(content)) return null;
        if (name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) return (name, content, "sql");
        if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && content.Contains("MigrationBuilder", StringComparison.Ordinal))
            return (name, content, "migration");
        return null;
    }

    public static string Category(string tool) => tool switch
    {
        _ when tool.StartsWith("prmake_work_task") => "task",
        _ when tool.StartsWith("prmake_work_item") || tool.StartsWith("prmake_tool") => "work-item",
        _ when tool.StartsWith("prmake_case") => "case",
        _ when tool.StartsWith("prmake_base") => "knowledge",
        _ when tool.StartsWith("prmake_devops") => "devops",
        _ when tool.StartsWith("prmake_pr") => "pr",
        "prmake_timeline" => "timeline",
        "prmake_ask" or "prmake_answer" or "prmake_cancel_questions" => "question",
        "prmake_advance" or "prmake_block" or "prmake_unblock" or "prmake_step" or "prmake_steps" or "prmake_checkpoint" => "step",
        _ => "plan"
    };

    /// <summary>Argumentos resumidos (≤ 1000) — entrada das regras e detalhe do evento.</summary>
    public static string? Summary(CallToolRequestParams? p)
    {
        if (p?.Arguments is not { Count: > 0 } args) return null;
        var text = string.Join(" · ", args.Select(a =>
        {
            var v = a.Value.ValueKind == JsonValueKind.String ? a.Value.GetString() ?? "" : a.Value.GetRawText();
            return $"{a.Key}: {(v.Length > 300 ? v[..300] + "…" : v)}";
        }));
        return text.Length <= 1000 ? text : text[..1000] + "…";
    }

    private static string? Str(CallToolRequestParams? p, string key) =>
        p?.Arguments is { } args && args.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(CallToolRequestParams? p, string key)
    {
        if (p?.Arguments is not { } args || !args.TryGetValue(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n > 0 ? n : null;
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString()?.Trim().TrimStart('#'), out var s) && s > 0 ? s : null;
    }

    private static string? Text(CallToolResult result)
    {
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text)).Trim();
        return text.Length == 0 ? null : text.Length <= 500 ? text : text[..500] + "…";
    }
}
