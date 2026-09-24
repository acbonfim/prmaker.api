namespace solvace.prform.domain.Entities;

/// <summary>
/// Valores de um usuário para um plugin de uso pessoal (<see cref="Plugin.IsPersonal"/>).
/// Os campos são as mesmas chaves da configuração global do plugin; <see cref="Options"/> guarda
/// o JSON { chave: valor } — valores sensíveis já chegam aqui protegidos (criptografados).
/// </summary>
public class UserPluginConfiguration : IEntity<int>, IAuditableEntity
{
    public int Id { get; set; }

    public int PluginId { get; private set; }
    public Plugin? Plugin { get; private set; }

    /// <summary>ExternalId do usuário (claim "ExternalId" da api-key).</summary>
    public Guid UserExternalId { get; private set; }

    public string Options { get; private set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    protected UserPluginConfiguration() { }

    public UserPluginConfiguration(int pluginId, Guid userExternalId, string options)
    {
        if (pluginId <= 0)
            throw new DomainException("Plugin is required");
        if (userExternalId == Guid.Empty)
            throw new DomainException("User is required");

        PluginId = pluginId;
        UserExternalId = userExternalId;
        SetOptions(options);
        CreatedAt = DateTime.UtcNow;
    }

    public void SetOptions(string options)
    {
        Options = string.IsNullOrWhiteSpace(options) ? "{}" : options;
        UpdatedAt = DateTime.UtcNow;
    }
}
