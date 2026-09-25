namespace solvace.prform.domain.Entities;

/// <summary>
/// Configuração de um campo do plugin (feature 0011), guardada em <see cref="Plugin.FieldSettings"/>.
/// </summary>
public class PluginFieldSetting
{
    /// <summary>Nome amigável exibido no lugar da chave ("Minhas integrações" e tela de plugins).</summary>
    public string? Label { get; set; }

    /// <summary>Campo do usuário que não é obrigatório: não conta para o plugin estar configurado.</summary>
    public bool Optional { get; set; }

    /// <summary>
    /// Campo do usuário opcional que, sem valor do usuário, usa o valor global como padrão. Sem esta
    /// flag o valor global é só uma sugestão na tela (o efetivo fica vazio até o usuário salvar).
    /// </summary>
    public bool UseGlobalDefault { get; set; }

    /// <summary>Campo fixo que não aparece em "Minhas integrações" (ex.: prompts longos).</summary>
    public bool Hidden { get; set; }
}
