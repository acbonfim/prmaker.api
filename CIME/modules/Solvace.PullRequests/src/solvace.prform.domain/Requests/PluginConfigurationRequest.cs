namespace solvace.prform.domain.Requests;

public class PluginConfigurationRequest
{
    public List<IDictionary<string, string>> Configurations { get; set; }

    public PluginConfigurationRequest()
    {
        
    }
}