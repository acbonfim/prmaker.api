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
    
    [StringLength(4000)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Options { get; set; }

    public PluginConfiguration(List<IDictionary<string, string>> request)
    {
        Options = JsonConvert.SerializeObject(request);
    }

    protected PluginConfiguration() { }
}