using System.Text.Json;

namespace solvace.prform.domain.Extensions;

public static class JsonExtensions
{
    public static IDictionary<string, string> JsonToDictionary(this string json)
    {
        var dictionary = JsonSerializer.Deserialize<IDictionary<string, string>>(json);
        return dictionary;
        
    }

    public static List<IDictionary<string, string>> JsonToListOfDictionaries(this string json)
    {
        var list = JsonSerializer.Deserialize<List<IDictionary<string, string>>>(json);
        return list!.Select(d => (IDictionary<string, string>)d).ToList();

    }
}