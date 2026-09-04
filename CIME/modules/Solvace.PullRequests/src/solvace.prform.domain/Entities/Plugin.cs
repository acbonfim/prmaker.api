using solvace.prform.domain.Requests;

namespace solvace.prform.domain.Entities;

public class Plugin: IEntity<int>, IDescribable, IAuditableEntity, ISoftDeletable
{
    protected Plugin() { }
    
    private const int MinDescriptionLength = 3;
    
    public int Id { get; set; }
    private string _description = string.Empty;
    public string Description 
    {
        get => _description;
        set => SetDescription(value);
    }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public PluginConfiguration Configurations { get; set; }

    // Quando true, só admins podem ler a configuração deste plugin.
    // Quando false, qualquer usuário logado pode ler.
    public bool AdminOnly { get; set; }

    public Plugin(PluginRequest request)
    {
        SetDescription(request.Description);
        CreatedAt = DateTime.UtcNow;
        AdminOnly = request.AdminOnly;
        SetConfigurations(request.Configurations);
    }

    public void SetAdminOnly(bool adminOnly)
    {
        AdminOnly = adminOnly;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void SetDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Description cannot be empty");
            
        if (description.Length < MinDescriptionLength)
            throw new DomainException($"Description must be at least {MinDescriptionLength} characters long");
        
        _description = description;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Delete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }

    public void SetConfigurations(IDictionary<string, string> configurations)
    {
        Configurations = new PluginConfiguration(new List<IDictionary<string, string>> { configurations });
    }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}