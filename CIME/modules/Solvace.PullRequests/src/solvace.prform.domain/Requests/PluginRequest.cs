using solvace.prform.domain.Entities;

namespace solvace.prform.domain.Requests;

public class PluginRequest
{
    public string Description { get; set; }
    public bool AdminOnly { get; set; }

    /// <summary>Uso pessoal: cada usuário preenche os próprios valores (ver Plugin.IsPersonal).</summary>
    public bool IsPersonal { get; set; }
    public IDictionary<string, string> Configurations { get; set; }
    
    public Plugin Create(PluginRequest request)
    {
        return new Plugin(request);
    }
}