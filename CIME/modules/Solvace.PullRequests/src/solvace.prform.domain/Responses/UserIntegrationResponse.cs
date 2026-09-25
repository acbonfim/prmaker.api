namespace solvace.prform.domain.Responses;

/// <summary>Um plugin de uso pessoal e os campos que o usuário preenche ("Minhas integrações").</summary>
public class UserIntegrationResponse
{
    public int PluginId { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>Todos os campos do plugin preenchidos pelo usuário.</summary>
    public bool Configured { get; set; }

    /// <summary>Opcional: sem configuração, só o recurso dele fica indisponível (não bloqueia a tela).</summary>
    public bool Optional { get; set; }

    public List<UserIntegrationFieldResponse> Fields { get; set; } = new();
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class UserIntegrationFieldResponse
{
    public string Key { get; set; } = string.Empty;

    /// <summary>Valor do usuário (ou sugestão do global, ver Suggested). Sempre null para campo sensível.</summary>
    public string? Value { get; set; }

    /// <summary>O usuário já salvou um valor não vazio para o campo.</summary>
    public bool HasValue { get; set; }

    /// <summary>Segredo: gravado criptografado e nunca devolvido.</summary>
    public bool Sensitive { get; set; }

    /// <summary>Value veio do plugin global como sugestão (o usuário ainda não salvou esse campo).</summary>
    public bool Suggested { get; set; }

    /// <summary>false = campo fixo, definido pelo administrador na configuração global (somente leitura).</summary>
    public bool Editable { get; set; } = true;
}

public class UserIntegrationStatusResponse
{
    /// <summary>Nenhum plugin de uso pessoal obrigatório pendente (opcionais não contam).</summary>
    public bool Ready { get; set; }
    public List<UserIntegrationPendingResponse> Pending { get; set; } = new();
}

public class UserIntegrationPendingResponse
{
    public int PluginId { get; set; }
    public string Description { get; set; } = string.Empty;
}
