using solvace.prform.domain.Entities;

namespace solvace.prform.domain.Requests;

public class PluginRequest
{
    public string Description { get; set; }
    public bool AdminOnly { get; set; }
    public IDictionary<string, string> Configurations { get; set; }
    
    public Plugin Create(PluginRequest request)
    {
        return new Plugin(request);
    }
}