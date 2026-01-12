using Newtonsoft.Json;
using solvace.prform.domain.Entities;

namespace solvace.prform.domain.Extensions;

public static class PluginConfigurationExtensions
{
    /// <summary>
    /// Obtém o valor de uma configuração pela chave
    /// </summary>
    /// <param name="configuration">A configuração do plugin</param>
    /// <param name="key">A chave da configuração</param>
    /// <returns>O valor da configuração ou null se não encontrado</returns>
    public static string? GetConfigurationValue(this PluginConfiguration configuration, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration?.Options))
            return null;

        try
        {
            var optionsList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(configuration.Options);
            
            if (optionsList == null || !optionsList.Any())
                return null;

            // Procura a chave no primeiro dicionário (ou em todos se necessário)
            var firstDict = optionsList[0];
            
            return firstDict.TryGetValue(key, out var value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Obtém todas as configurações como um dicionário
    /// </summary>
    /// <param name="configuration">A configuração do plugin</param>
    /// <returns>Dicionário com todas as configurações</returns>
    public static IDictionary<string, string>? GetAllConfigurations(this PluginConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration?.Options))
            return null;

        try
        {
            var optionsList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(configuration.Options);
            return optionsList?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Verifica se uma configuração existe
    /// </summary>
    /// <param name="configuration">A configuração do plugin</param>
    /// <param name="key">A chave da configuração</param>
    /// <returns>True se a chave existe, False caso contrário</returns>
    public static bool HasConfiguration(this PluginConfiguration configuration, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration?.Options))
            return false;

        try
        {
            var optionsList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(configuration.Options);
            return optionsList?.FirstOrDefault()?.ContainsKey(key) ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Obtém o valor de uma configuração com um valor padrão se não encontrado
    /// </summary>
    /// <param name="configuration">A configuração do plugin</param>
    /// <param name="key">A chave da configuração</param>
    /// <param name="defaultValue">Valor padrão a retornar se a chave não for encontrada</param>
    /// <returns>O valor da configuração ou o valor padrão</returns>
    public static string GetConfigurationValueOrDefault(this PluginConfiguration configuration, string key, string defaultValue)
    {
        return configuration.GetConfigurationValue(key) ?? defaultValue;
    }
}