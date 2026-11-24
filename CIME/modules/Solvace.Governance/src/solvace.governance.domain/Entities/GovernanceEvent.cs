namespace solvace.governance.domain.Entities;

/// <summary>
/// 0098: um registro da trilha de governança — toda gravação (tela/API), chamada de ferramenta do MCP e evento de hook
/// do Claude Code, com quem, origem, card, plano/item/sub-tarefa, duração, resultado e as regras que casaram.
/// </summary>
public class GovernanceEvent
{
    public const int MaxActionLength = 300;
    public const int MaxDetailLength = 2_000;
    public const int MaxNameLength = 200;

    public long Id { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public int? DurationMs { get; private set; }
    public Guid? UserId { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public string? Role { get; private set; }
    public string Source { get; private set; } = GovernanceSource.Api;
    public string Action { get; private set; } = string.Empty;
    public string Category { get; private set; } = "other";
    public int? CardNumber { get; private set; }
    public Guid? PlanId { get; private set; }
    public string? Phase { get; private set; }
    public string? StepKey { get; private set; }
    public int? WorkItemNumber { get; private set; }
    public int? TaskNumber { get; private set; }
    public string Outcome { get; private set; } = GovernanceOutcome.Ok;
    public int? StatusCode { get; private set; }
    public string[] Rules { get; private set; } = [];
    public string? Detail { get; private set; }
    public string? SessionId { get; private set; }
    public string? Machine { get; private set; }

    protected GovernanceEvent() { }

    public GovernanceEvent(GovernanceEventData d)
    {
        StartedAt = d.StartedAt;
        FinishedAt = d.FinishedAt;
        DurationMs = d.DurationMs ?? (d.FinishedAt is { } f ? (int)Math.Clamp((f - d.StartedAt).TotalMilliseconds, 0, int.MaxValue) : null);
        UserId = d.UserId;
        UserName = Clean(d.UserName, MaxNameLength) ?? "Desconhecido";
        Role = Clean(d.Role, 100);
        Source = GovernanceSource.IsValid(d.Source) ? d.Source : GovernanceSource.Api;
        Action = Clean(d.Action, MaxActionLength) ?? "-";
        Category = Clean(d.Category, 40) ?? "other";
        CardNumber = d.CardNumber is > 0 ? d.CardNumber : null;
        PlanId = d.PlanId;
        Phase = Clean(d.Phase, 20);
        StepKey = Clean(d.StepKey, 100);
        WorkItemNumber = d.WorkItemNumber is > 0 ? d.WorkItemNumber : null;
        TaskNumber = d.TaskNumber is > 0 ? d.TaskNumber : null;
        Outcome = GovernanceOutcome.All.Contains(d.Outcome) ? d.Outcome : GovernanceOutcome.Ok;
        StatusCode = d.StatusCode;
        Rules = d.Rules?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().Take(20).ToArray() ?? [];
        Detail = Clean(d.Detail, MaxDetailLength);
        SessionId = Clean(d.SessionId, 100);
        Machine = Clean(d.Machine, MaxNameLength);
    }

    public static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length <= max ? v : v[..(max - 1)] + "…";
    }
}

/// <summary>Dados de entrada de um evento (o sink monta a entidade fora da requisição).</summary>
public sealed record GovernanceEventData
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public int? DurationMs { get; init; }
    public Guid? UserId { get; init; }
    public string? UserName { get; init; }
    public string? Role { get; init; }
    public string Source { get; init; } = GovernanceSource.Api;
    public string Action { get; init; } = string.Empty;
    public string Category { get; init; } = "other";
    public int? CardNumber { get; init; }
    public Guid? PlanId { get; init; }
    public string? Phase { get; init; }
    public string? StepKey { get; init; }
    public int? WorkItemNumber { get; init; }
    public int? TaskNumber { get; init; }
    public string Outcome { get; init; } = GovernanceOutcome.Ok;
    public int? StatusCode { get; init; }
    public IReadOnlyList<string>? Rules { get; init; }
    public string? Detail { get; init; }
    public string? SessionId { get; init; }
    public string? Machine { get; init; }
}
