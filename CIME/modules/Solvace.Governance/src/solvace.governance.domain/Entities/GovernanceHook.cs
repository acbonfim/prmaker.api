namespace solvace.governance.domain.Entities;

/// <summary>
/// 0098: hook configurável. <c>claude:*</c> = hooks do Claude Code que o instalador das skills registra (um script só,
/// que lê esta configuração); <c>server:&lt;glob da ação&gt;</c> = gatilho do servidor sobre os eventos da trilha.
/// </summary>
public class GovernanceHook
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Event { get; private set; } = string.Empty;
    /// <summary>Glob da ferramenta (claude:*ToolUse) ou da categoria/resultado (server:*); vazio = tudo.</summary>
    public string Matcher { get; private set; } = string.Empty;
    public string Action { get; private set; } = GovernanceHookAction.Record;
    public Dictionary<string, string> Config { get; private set; } = [];
    public bool Enabled { get; private set; }
    public bool Builtin { get; private set; }
    public int Order { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    protected GovernanceHook() { }

    public GovernanceHook(Guid id, string key, bool builtin)
    {
        Id = id;
        Key = GovernanceRule.NormalizeKey(key);
        Builtin = builtin;
    }

    public void Update(string name, string? description, string evt, string? matcher, string action,
        IDictionary<string, string>? config, bool enabled, int order, string actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Informe o nome do hook.");
        if (!GovernanceHookEvent.IsValid(evt)) throw new DomainException($"Evento inválido: {evt}.");
        if (!GovernanceHookAction.IsValid(action)) throw new DomainException($"Ação inválida: {action}.");
        var cfg = (config ?? new Dictionary<string, string>())
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value ?? string.Empty);
        if (action == GovernanceHookAction.Webhook)
        {
            if (!cfg.TryGetValue("url", out var url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new DomainException("O webhook precisa de uma url https em config.url.");
        }
        if (action == GovernanceHookAction.Evaluate && evt != "claude:PreToolUse")
            throw new DomainException("A ação evaluate só vale no claude:PreToolUse.");
        Name = name.Trim();
        Description = (description ?? string.Empty).Trim();
        Event = evt;
        Matcher = (matcher ?? string.Empty).Trim();
        Action = action;
        Config = cfg;
        Enabled = enabled;
        Order = order;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    /// <summary>Configuração sem segredos (para devolver a quem não é admin, ex.: o script do hook).</summary>
    public Dictionary<string, string> PublicConfig() =>
        Config.Where(kv => !kv.Key.Contains("secret", StringComparison.OrdinalIgnoreCase)
                           && !kv.Key.Contains("token", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
}
