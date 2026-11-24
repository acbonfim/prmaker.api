namespace solvace.governance.domain.Entities;

/// <summary>0098: configurações da governança (uma linha só, Id = 1).</summary>
public class GovernanceSettings
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public string Mode { get; private set; } = GovernanceMode.Monitor;
    public int RetentionDays { get; private set; } = 365;
    public bool HookFailOpen { get; private set; } = true;
    public int HookTimeoutMs { get; private set; } = 1_500;
    public int HookCacheSeconds { get; private set; } = 60;
    public List<string> ExemptRoles { get; private set; } = [];
    public List<string> RecordSources { get; private set; } = [.. GovernanceSource.All];
    public List<Milestone> Milestones { get; private set; } = DefaultMilestones();
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public GovernanceSettings() { }

    public void Update(string mode, int retentionDays, bool hookFailOpen, int hookTimeoutMs, int hookCacheSeconds,
        IEnumerable<string>? exemptRoles, IEnumerable<string>? recordSources, IEnumerable<Milestone>? milestones,
        string actor, DateTimeOffset now)
    {
        if (!GovernanceMode.IsValid(mode)) throw new DomainException($"Modo inválido: {mode}.");
        if (retentionDays is < 30 or > 3650) throw new DomainException("A retenção vai de 30 a 3650 dias.");
        if (hookTimeoutMs is < 200 or > 10_000) throw new DomainException("O tempo máximo do hook vai de 200 a 10000 ms.");
        if (hookCacheSeconds is < 0 or > 3600) throw new DomainException("O cache do hook vai de 0 a 3600 s.");
        var ms = (milestones ?? []).ToList();
        foreach (var m in ms) m.Validate();
        if (ms.Select(m => m.Key).Distinct().Count() != ms.Count) throw new DomainException("Há marcos com a mesma chave.");

        Mode = mode;
        RetentionDays = retentionDays;
        HookFailOpen = hookFailOpen;
        HookTimeoutMs = hookTimeoutMs;
        HookCacheSeconds = hookCacheSeconds;
        ExemptRoles = RuleScope.Clean(exemptRoles, lower: true);
        RecordSources = RuleScope.Clean(recordSources, lower: true).Where(GovernanceSource.IsValid).ToList();
        Milestones = ms.OrderBy(m => m.Order).ToList();
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    /// <summary>Marcos padrão da linha do tempo do card.</summary>
    public static List<Milestone> DefaultMilestones() =>
    [
        new() { Key = "task-started", Label = "Iniciou a tarefa", Order = 10, From = Milestone.FromPlan, Phase = "analysis", At = Milestone.AtCreated },
        new() { Key = "analysis-started", Label = "Análise iniciada", Order = 20, From = Milestone.FromPlan, Phase = "analysis", At = Milestone.AtStarted },
        new() { Key = "prototype", Label = "Protótipo inicial", Order = 30, From = Milestone.FromStep, Match = "*prototip*", At = Milestone.AtFinished },
        new() { Key = "analysis-done", Label = "Análise inicial concluída", Order = 40, From = Milestone.FromPlan, Phase = "analysis", At = Milestone.AtFinished },
        new() { Key = "correction-started", Label = "Correção iniciada", Order = 50, From = Milestone.FromPlan, Phase = "correction", At = Milestone.AtStarted },
        new() { Key = "pr-opened", Label = "PR aberto", Order = 60, From = Milestone.FromTask, Match = "pr", At = Milestone.AtFinished },
        new() { Key = "qa", Label = "Em QA", Order = 70, From = Milestone.FromEvent, Match = "* Azure/card/*/actions/ready-for-qa", At = Milestone.AtCreated },
        new() { Key = "closed", Label = "Concluído", Order = 80, From = Milestone.FromPlan, Phase = "correction", At = Milestone.AtFinished }
    ];
}

/// <summary>Marco da linha do tempo do card: de onde vem a data.</summary>
public sealed class Milestone
{
    public const string FromPlan = "plan";
    public const string FromStep = "step";
    public const string FromWorkItem = "work-item";
    public const string FromTask = "task";
    public const string FromEvent = "event";
    public const string FromRequest = "request";
    public const string AtCreated = "created";
    public const string AtStarted = "started";
    public const string AtFinished = "finished";

    private static readonly string[] Froms = [FromPlan, FromStep, FromWorkItem, FromTask, FromEvent, FromRequest];
    private static readonly string[] Ats = [AtCreated, AtStarted, AtFinished];

    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Order { get; set; }
    public string From { get; set; } = FromPlan;
    /// <summary>analysis | correction (plan/step).</summary>
    public string? Phase { get; set; }
    /// <summary>Glob: chave/tipo da etapa, tipo do item, tipo da sub-tarefa, ação do evento ou tipo do pedido.</summary>
    public string? Match { get; set; }
    public string At { get; set; } = AtCreated;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(Label)) throw new DomainException("Todo marco precisa de chave e rótulo.");
        if (!Froms.Contains(From)) throw new DomainException($"Origem de marco inválida: {From}.");
        if (!Ats.Contains(At)) throw new DomainException($"Momento de marco inválido: {At}.");
        if (Phase is not null and not ("analysis" or "correction")) throw new DomainException($"Fase de marco inválida: {Phase}.");
        if (From is FromStep or FromWorkItem or FromTask or FromEvent or FromRequest && string.IsNullOrWhiteSpace(Match))
            throw new DomainException($"O marco {Key} precisa de um padrão (match).");
    }
}
