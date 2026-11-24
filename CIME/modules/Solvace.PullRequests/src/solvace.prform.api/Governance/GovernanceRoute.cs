using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing.Patterns;

namespace solvace.prform.Governance;

/// <summary>
/// 0098: a ação de uma requisição HTTP para a trilha e as regras — método + template da rota sem o prefixo
/// <c>api/v{version}/</c>, com os valores que NÃO são identificadores preenchidos (<c>{verb}</c> → <c>complete</c>,
/// <c>{action}</c> → <c>ready-for-qa</c>), para agrupar por ação e ainda distinguir o que foi feito. Card, plano,
/// item e sub-tarefa saem dos valores da rota.
/// </summary>
public static partial class GovernanceRoute
{
    public sealed record Info(string Action, string Category, int? Card, Guid? PlanId, string? StepKey, int? WorkItem, int? Task);

    [GeneratedRegex(@"\{(?<name>[^}:?=]+)[^}]*\}")]
    private static partial Regex Parameter();

    private static readonly HashSet<string> IdNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "card", "cardNumber", "number", "taskNumber", "planId", "questionId", "noteId", "linkId", "artifactId",
        "suggestionId", "pluginId", "externalId", "userId", "reference", "version", "key"
    };

    public static Info Describe(HttpContext http)
    {
        var method = http.Request.Method.ToUpperInvariant();
        var values = http.Request.RouteValues;
        var raw = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        string template;
        if (raw is null)
        {
            template = http.Request.Path.Value?.Trim('/') ?? string.Empty;
        }
        else
        {
            template = Parameter().Replace(raw.Trim('/'), m =>
            {
                var name = m.Groups["name"].Value;
                if (name == "controller" || name == "action") return values[name]?.ToString() ?? m.Value;
                if (IdNames.Contains(name)) return "{" + name + "}";
                var v = values[name]?.ToString();
                return string.IsNullOrEmpty(v) || v.Length > 60 || Guid.TryParse(v, out _) || long.TryParse(v, out _) ? "{" + name + "}" : v;
            });
        }
        // api/v{version}/ExecutionPlan/... → ExecutionPlan/...
        template = Regex.Replace(template, @"^api/v[^/]*/", string.Empty, RegexOptions.IgnoreCase);
        var controller = values["controller"]?.ToString() ?? template.Split('/')[0];

        int? card = Int(values, "card") ?? Int(values, "cardNumber");
        Guid? plan = GuidOf(values, "planId");
        int? item = null, task = null;
        string? step = values["key"]?.ToString();
        switch (controller)
        {
            case "Azure" or "Timeline" or "DevOps":
                card ??= Int(values, "id");
                break;
            case "ExecutionPlan" or "CardReset":
                plan ??= GuidOf(values, "id");
                break;
            case "WorkItems":
                item = Int(values, "number");
                task = Int(values, "taskNumber");
                break;
        }
        card ??= QueryInt(http, "card") ?? QueryInt(http, "cardNumber");

        var action = $"{method} {template}";
        return new Info(action.Length > 300 ? action[..300] : action, Category(controller, template), card, plan, step, item, task);
    }

    public static string Category(string controller, string template)
    {
        var t = template.ToLowerInvariant();
        return controller switch
        {
            "ExecutionPlan" when t.Contains("/questions") => "question",
            "ExecutionPlan" when t.Contains("/steps") => "step",
            "ExecutionPlan" or "CardReset" => "plan",
            "WorkItems" when t.Contains("/tasks") => "task",
            "WorkItems" => "work-item",
            "ExecutionQueue" or "ExecutionWorker" => "queue",
            "Azure" or "DevOps" => "devops",
            "GitHub" or "PullRequest" or "Promotion" => "pr",
            "Timeline" => "timeline",
            "CaseChat" or "Case" => "case",
            "PluginConfiguration" or "PluginSettings" or "Skills" => "config",
            "Governance" => "governance",
            "ReverseEngineering" or "Knowledge" or "Architecture" => "knowledge",
            "Handover" => "handover",
            "Teams" => "timeline",
            "Vacations" => "vacations",
            "UserIntegration" => "config",
            "AI" or "AiUsage" => "ai",
            _ => "other"
        };
    }

    private static int? Int(RouteValueDictionary values, string key) =>
        int.TryParse(values[key]?.ToString()?.TrimStart('#'), out var n) && n > 0 ? n : null;

    private static Guid? GuidOf(RouteValueDictionary values, string key) =>
        System.Guid.TryParse(values[key]?.ToString(), out var g) ? g : null;

    private static int? QueryInt(HttpContext http, string key) =>
        int.TryParse(http.Request.Query[key].ToString().TrimStart('#'), out var n) && n > 0 ? n : null;
}
