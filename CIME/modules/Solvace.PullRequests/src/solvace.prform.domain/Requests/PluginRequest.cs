using solvace.prform.domain.Entities;

namespace solvace.prform.domain.Requests;

public class PluginRequest
{
    public string Description { get; set; }
    public bool AdminOnly { get; set; }

    /// <summary>Uso pessoal: cada usuário preenche os próprios valores (ver Plugin.IsPersonal).</summary>
    public bool IsPersonal { get; set; }

    /// <summary>Plugin pessoal: chaves que o usuário preenche (null = todas; as demais ficam com o valor global).</summary>
    public List<string>? PersonalFields { get; set; }

    /// <summary>Plugin pessoal opcional: não bloqueia a tela de PR quando o usuário não configurou.</summary>
    public bool IsOptional { get; set; }

    /// <summary>Configuração por campo (nome amigável, opcional, padrão global, oculto). null = mantém a atual.</summary>
    public Dictionary<string, PluginFieldSetting>? FieldSettings { get; set; }

    public IDictionary<string, string> Configurations { get; set; }
    
    public Plugin Create(PluginRequest request)
    {
        return new Plugin(request);
    }
}