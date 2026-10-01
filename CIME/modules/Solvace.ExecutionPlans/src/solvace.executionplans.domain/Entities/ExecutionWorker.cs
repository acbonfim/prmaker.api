namespace solvace.executionplans.domain.Entities;

public static class ExecutionWorkerStatus
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Revoked = "revoked";
}

/// <summary>
/// Executor (0039): uma máquina de um usuário com o <c>prmake-agent</c> rodando. Pega os pedidos do dono, roda o Claude
/// Code sem terminal e dá sinal de vida. Tem credencial própria (<see cref="CredentialId"/> = <c>jti</c> do token),
/// revogável na tela "Meus executores".
/// </summary>
public class ExecutionWorker
{
    public const int MaxNameLength = 200;
    public const int MaxJsonLength = 64_000;
    public const int MaxConcurrencyLimit = 3;

    /// <summary>Sem sinal por esse tempo = offline.</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);

    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string OwnerName { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Host { get; private set; } = string.Empty;
    public string? Os { get; private set; }
    public string? AgentVersion { get; private set; }
    public string? ClaudeVersion { get; private set; }
    public string? SkillsVersion { get; private set; }
    public string? Workspace { get; private set; }
    public int MaxConcurrency { get; private set; } = 1;
    public string Status { get; private set; } = ExecutionWorkerStatus.Active;

    /// <summary>jti da credencial válida — registrar de novo troca e invalida a anterior.</summary>
    public Guid CredentialId { get; private set; }

    /// <summary>JSON do executor: repositórios mapeados, aliases de banco alcançáveis etc.</summary>
    public string? Capabilities { get; private set; }

    /// <summary>JSON do último <c>prmake-agent doctor</c>: [{ name, ok, message }].</summary>
    public string? Doctor { get; private set; }
    public DateTimeOffset? DoctorAt { get; private set; }
    /// <summary>"Rodar diagnóstico agora" pela tela: pendente enquanto não chega um doctor mais novo.</summary>
    public DateTimeOffset? DoctorRequestedAt { get; private set; }
    /// <summary>Limite de uso da conta do Claude atingido (0041): a máquina não pega pedidos até este horário.</summary>
    public DateTimeOffset? ThrottledUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedBy { get; private set; }

    public uint Version { get; private set; }

    protected ExecutionWorker() { }

    public ExecutionWorker(Guid ownerUserId, string ownerName, string? name, string host, string? os, DateTimeOffset now)
    {
        var h = Clean(host, MaxNameLength) ?? throw new DomainException("Informe o nome da máquina.");
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        OwnerName = Clean(ownerName, 200) ?? "Usuário";
        Host = h;
        Name = Clean(name, MaxNameLength) ?? h;
        Os = Clean(os, 100);
        CredentialId = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
        LastSeenAt = now;
    }

    public bool IsRevoked => Status == ExecutionWorkerStatus.Revoked;
    public bool IsThrottled(DateTimeOffset now) => ThrottledUntil is { } until && until > now;

    public void Throttle(DateTimeOffset until, DateTimeOffset now)
    {
        ThrottledUntil = until;
        UpdatedAt = now;
    }

    public bool DoctorPending => DoctorRequestedAt is { } requested && (DoctorAt is null || DoctorAt < requested);
    public bool IsOnline(DateTimeOffset now) => !IsRevoked && LastSeenAt is { } seen && now - seen <= OnlineWindow;
    public bool AcceptsWork(DateTimeOffset now) => Status == ExecutionWorkerStatus.Active && IsOnline(now);

    /// <summary>Registrar de novo a mesma máquina: credencial nova (a antiga deixa de valer) e volta a ativo.</summary>
    public void Reactivate(string? os, DateTimeOffset now)
    {
        CredentialId = Guid.NewGuid();
        Status = ExecutionWorkerStatus.Active;
        RevokedAt = null;
        RevokedBy = null;
        Os = Clean(os, 100) ?? Os;
        LastSeenAt = now;
        UpdatedAt = now;
    }

    /// <summary>Sinal de vida do executor (long-poll ou heartbeat) com o que ele informa sobre si.</summary>
    public void Seen(DateTimeOffset now) => LastSeenAt = now;

    public void Report(string? agentVersion, string? claudeVersion, string? skillsVersion, string? workspace,
        string? capabilities, DateTimeOffset now)
    {
        AgentVersion = Clean(agentVersion, 50) ?? AgentVersion;
        ClaudeVersion = Clean(claudeVersion, 100) ?? ClaudeVersion;
        SkillsVersion = Clean(skillsVersion, 100) ?? SkillsVersion;
        Workspace = Clean(workspace, 500) ?? Workspace;
        if (capabilities is not null) Capabilities = Json(capabilities);
        LastSeenAt = now;
        UpdatedAt = now;
    }

    public void SetDoctor(string doctor, DateTimeOffset now)
    {
        Doctor = Json(doctor);
        DoctorAt = now;
        LastSeenAt = now;
        UpdatedAt = now;
    }

    public void RequestDoctor(DateTimeOffset now)
    {
        EnsureNotRevoked();
        DoctorRequestedAt = now;
        UpdatedAt = now;
    }

    public void Configure(string? name, int? maxConcurrency, DateTimeOffset now)
    {
        if (name is not null)
            Name = Clean(name, MaxNameLength) ?? throw new DomainException("O nome da máquina não pode ficar vazio.");
        if (maxConcurrency is { } max)
        {
            if (max < 1 || max > MaxConcurrencyLimit)
                throw new DomainException($"A concorrência vai de 1 a {MaxConcurrencyLimit}.");
            MaxConcurrency = max;
        }
        UpdatedAt = now;
    }

    public void Pause(DateTimeOffset now)
    {
        EnsureNotRevoked();
        Status = ExecutionWorkerStatus.Paused;
        UpdatedAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        EnsureNotRevoked();
        Status = ExecutionWorkerStatus.Active;
        UpdatedAt = now;
    }

    public void Revoke(string actor, DateTimeOffset now)
    {
        Status = ExecutionWorkerStatus.Revoked;
        RevokedAt = now;
        RevokedBy = Clean(actor, 200);
        CredentialId = Guid.NewGuid();
        UpdatedAt = now;
    }

    private void EnsureNotRevoked()
    {
        if (IsRevoked)
            throw new DomainException("Este executor foi revogado — registre a máquina de novo (prmake-agent register).");
    }

    private static string Json(string value)
    {
        var v = value.Trim();
        if (v.Length > MaxJsonLength)
            throw new DomainException("Informações do executor grandes demais.");
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(v);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new DomainException("Informações do executor em JSON inválido.");
        }
        return v;
    }

    private static string? Clean(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }
}

/// <summary>
/// Configurações do usuário para a fila (0039): orçamento diário e a regra que pede análises sozinha a partir do
/// Azure DevOps (desligada por padrão).
/// </summary>
public class ExecutionUserSettings
{
    public const int MaxListItems = 30;

    public Guid UserId { get; private set; }

    /// <summary>US$ por dia nos pedidos do executor (null = sem limite).</summary>
    public decimal? DailyBudgetUsd { get; private set; }

    public bool AutoAnalyzeEnabled { get; private set; }
    public List<string> AutoWorkItemTypes { get; private set; } = [];
    public List<string> AutoStates { get; private set; } = [];
    public List<string> AutoAreaPaths { get; private set; } = [];
    /// <summary>"@Me" (dono do PAT) ou o e-mail/nome no DevOps.</summary>
    public string? AutoAssignedTo { get; private set; }
    public int AutoMaxPerDay { get; private set; } = 5;
    public DateTimeOffset? AutoLastCheckAt { get; private set; }
    public string? AutoLastError { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    protected ExecutionUserSettings() { }

    public ExecutionUserSettings(Guid userId, DateTimeOffset now)
    {
        UserId = userId;
        UpdatedAt = now;
    }

    public void SetBudget(decimal? dailyBudgetUsd, DateTimeOffset now)
    {
        if (dailyBudgetUsd is < 0 or > 10_000)
            throw new DomainException("Orçamento diário inválido (0 a 10.000 US$, vazio = sem limite).");
        DailyBudgetUsd = dailyBudgetUsd;
        UpdatedAt = now;
    }

    public void SetAutoRule(bool enabled, IEnumerable<string>? types, IEnumerable<string>? states, IEnumerable<string>? areas,
        string? assignedTo, int? maxPerDay, DateTimeOffset now)
    {
        var t = CleanList(types);
        var s = CleanList(states);
        if (enabled && (t.Count == 0 || s.Count == 0))
            throw new DomainException("Para ligar a regra, informe ao menos um tipo de item e um estado.");
        if (maxPerDay is < 1 or > 50)
            throw new DomainException("Máximo de pedidos automáticos por dia: de 1 a 50.");

        AutoAnalyzeEnabled = enabled;
        AutoWorkItemTypes = t;
        AutoStates = s;
        AutoAreaPaths = CleanList(areas);
        var who = assignedTo?.Trim();
        AutoAssignedTo = string.IsNullOrEmpty(who) ? "@Me" : who.Length > 200 ? who[..200] : who;
        AutoMaxPerDay = maxPerDay ?? AutoMaxPerDay;
        AutoLastError = null;
        UpdatedAt = now;
    }

    /// <summary>Pode avaliar a regra agora (no máximo a cada <paramref name="interval"/>)?</summary>
    public bool AutoRuleDue(DateTimeOffset now, TimeSpan interval) =>
        AutoAnalyzeEnabled && (AutoLastCheckAt is null || now - AutoLastCheckAt >= interval);

    public void MarkAutoChecked(string? error, DateTimeOffset now)
    {
        AutoLastCheckAt = now;
        AutoLastError = error is null ? null : error.Length > 1000 ? error[..1000] : error;
    }

    private static List<string> CleanList(IEnumerable<string>? values) =>
        (values ?? [])
        .Select(v => v?.Trim() ?? string.Empty)
        .Where(v => v.Length > 0 && v.Length <= 200 && !v.Contains('\''))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxListItems)
        .ToList();
}
