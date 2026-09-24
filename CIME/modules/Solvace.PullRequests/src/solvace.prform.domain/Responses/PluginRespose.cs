namespace solvace.prform.domain.Responses;

public class PluginRespose
{
    public int Id { get; set; }
    public string Description { get; set; }
    public bool AdminOnly { get; set; }
    public bool IsPersonal { get; set; }
    public IReadOnlyList<string>? PersonalFields { get; set; }
    public bool IsOptional { get; set; }

    public IDictionary<string, string> Configurations { get; set; }
}