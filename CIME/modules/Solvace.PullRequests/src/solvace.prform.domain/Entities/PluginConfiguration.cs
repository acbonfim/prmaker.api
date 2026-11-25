using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using solvace.prform.domain.Requests;

namespace solvace.prform.domain.Entities;

public class PluginConfiguration: IEntity<int>
{
    public int Id { get; set; }
    
    public int PluginId { get; set; }
    public Plugin Plugin { get; set; }
    
    // longtext (feature 0011): os prompts do "AI Configurations" passavam de 4000 caracteres.
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Options { get; set; }

    /// <summary>
    /// 0086: versão da linha (xmin do PostgreSQL) — token de concorrência do PATCH das configurações: dois admins
    /// salvando ao mesmo tempo não se sobrescrevem. Não é coluna (coluna de sistema).
    /// </summary>
    public uint Version { get; set; }

    public PluginConfiguration(List<IDictionary<string, string>> request)
    {
        Options = JsonConvert.SerializeObject(request);
    }

    protected PluginConfiguration() { }
}