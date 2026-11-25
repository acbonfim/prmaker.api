using solvace.prform.domain.Entities;

namespace solvace.prform.application.Contracts;

/// <summary>
/// Histórico das alterações das chaves de plugin (0086). Implementado no host (sabe pelo catálogo quais chaves são
/// segredo). Só ADICIONA as linhas ao contexto — quem chama grava junto com a alteração (mesmo SaveChanges).
/// </summary>
public interface IPluginSettingHistory
{
    /// <summary>Uma linha por chave que mudou entre <paramref name="before"/> e <paramref name="after"/> (diferença chave a chave).</summary>
    void Track(Plugin plugin, IDictionary<string, string>? before, IDictionary<string, string>? after);
}

/// <summary>Diferença chave a chave entre dois dicionários de configuração (0086).</summary>
public static class PluginSettingDiff
{
    /// <summary>
    /// (chave, antes, depois) de cada chave que mudou. Antes null = a chave não existia; depois null = removida.
    /// Mesma chave com maiúsculas diferentes conta como chaves diferentes (os leitores diferenciam).
    /// </summary>
    public static IReadOnlyList<(string Key, string? Old, string? New)> Compute(
        IDictionary<string, string>? before, IDictionary<string, string>? after)
    {
        before ??= new Dictionary<string, string>();
        after ??= new Dictionary<string, string>();
        var changes = new List<(string, string?, string?)>();
        foreach (var (key, value) in after)
        {
            if (!before.TryGetValue(key, out var old))
                changes.Add((key, null, value ?? string.Empty));
            else if (!string.Equals(old ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal))
                changes.Add((key, old ?? string.Empty, value ?? string.Empty));
        }
        foreach (var (key, old) in before)
            if (!after.ContainsKey(key))
                changes.Add((key, old ?? string.Empty, null));
        return changes;
    }
}
