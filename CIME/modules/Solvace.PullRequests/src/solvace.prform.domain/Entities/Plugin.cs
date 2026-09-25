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

    /// <summary>
    /// Uso pessoal: cada usuário preenche os mesmos campos deste plugin com os próprios valores
    /// (ex.: token), guardados em <see cref="UserPluginConfiguration"/>. A configuração global
    /// passa a servir de modelo dos campos. Plugins não pessoais são lidos como sempre.
    /// </summary>
    public bool IsPersonal { get; set; }

    /// <summary>
    /// Plugin pessoal: JSON com as chaves que CADA USUÁRIO preenche. As demais são fixas — valem
    /// os valores desta configuração global e o usuário só as vê. null = todas as chaves são do
    /// usuário (comportamento original).
    /// </summary>
    public string? PersonalFields { get; set; }

    /// <summary>
    /// Plugin pessoal opcional (feature 0007): aparece em "Minhas integrações", mas a falta de
    /// configuração não bloqueia a tela de PR — só o recurso que depende dele (ex.: Teams).
    /// </summary>
    public bool IsOptional { get; set; }

    /// <summary>
    /// JSON com a configuração de cada campo (feature 0011): nome amigável, campo do usuário
    /// opcional, uso do valor global como padrão e campo fixo oculto. Ver <see cref="PluginFieldSetting"/>.
    /// null = nenhum campo configurado (comportamento original).
    /// </summary>
    public string? FieldSettings { get; set; }

    /// <summary>Configuração dos campos por chave (sem diferenciar maiúsculas).</summary>
    public IReadOnlyDictionary<string, PluginFieldSetting> GetFieldSettings()
    {
        var empty = new Dictionary<string, PluginFieldSetting>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(FieldSettings))
            return empty;
        try
        {
            var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, PluginFieldSetting>>(FieldSettings);
            return parsed is null
                ? empty
                : new Dictionary<string, PluginFieldSetting>(parsed.Where(p => p.Value is not null), StringComparer.OrdinalIgnoreCase);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return empty;
        }
    }

    private PluginFieldSetting? GetFieldSetting(string key) =>
        GetFieldSettings().TryGetValue(key, out var setting) ? setting : null;

    /// <summary>Nome amigável do campo (null = sem nome definido; exibir a chave).</summary>
    public string? GetFieldLabel(string key) =>
        GetFieldSetting(key)?.Label is { Length: > 0 } label ? label : null;

    /// <summary>Campo do usuário que não é obrigatório para o plugin estar configurado.</summary>
    public bool IsOptionalField(string key) => IsUserField(key) && GetFieldSetting(key)?.Optional == true;

    /// <summary>Campo do usuário opcional que cai para o valor global quando o usuário não preencheu.</summary>
    public bool UsesGlobalDefault(string key) => IsOptionalField(key) && GetFieldSetting(key)?.UseGlobalDefault == true;

    /// <summary>Campo fixo oculto em "Minhas integrações".</summary>
    public bool IsHiddenField(string key) => !IsUserField(key) && GetFieldSetting(key)?.Hidden == true;

    /// <summary>Define a configuração dos campos (null = remove).</summary>
    public void SetFieldSettings(IDictionary<string, PluginFieldSetting>? settings)
    {
        FieldSettings = settings is null
            ? null
            : Newtonsoft.Json.JsonConvert.SerializeObject(settings
                .Where(s => !string.IsNullOrWhiteSpace(s.Key) && s.Value is not null)
                .GroupBy(s => s.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Value));
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Chaves preenchidas pelo usuário (null = todas).</summary>
    public IReadOnlyList<string>? GetPersonalFieldKeys()
    {
        if (string.IsNullOrWhiteSpace(PersonalFields))
            return null;
        try
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(PersonalFields);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>O campo é preenchido pelo usuário (true) ou fixo, vindo desta configuração global (false).</summary>
    public bool IsUserField(string key)
    {
        if (!IsPersonal)
            return false;
        var keys = GetPersonalFieldKeys();
        return keys is null || keys.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Define as chaves preenchidas pelo usuário (null = todas).</summary>
    public void SetPersonalFields(IEnumerable<string>? keys)
    {
        PersonalFields = keys is null
            ? null
            : Newtonsoft.Json.JsonConvert.SerializeObject(keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
        UpdatedAt = DateTime.UtcNow;
    }

    public Plugin(PluginRequest request)
    {
        SetDescription(request.Description);
        CreatedAt = DateTime.UtcNow;
        AdminOnly = request.AdminOnly;
        IsPersonal = request.IsPersonal;
        IsOptional = request.IsOptional;
        SetPersonalFields(request.PersonalFields);
        SetFieldSettings(request.FieldSettings);
        SetConfigurations(request.Configurations);
    }

    public void SetPersonal(bool isPersonal)
    {
        IsPersonal = isPersonal;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetOptional(bool isOptional)
    {
        IsOptional = isOptional;
        UpdatedAt = DateTime.UtcNow;
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