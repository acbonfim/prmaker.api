using System.Text.Json;

namespace solvace.prform.domain.Promotion;

/// <summary>Um PR do card registrado no PRMake: o id do registro, a branch de origem (prefixo + nome) e o destino.</summary>
public record RegisteredBranch(int Id, string Branch, string Target);

/// <summary>
/// 0074: a branch de tratamento do card e a base de onde ela saiu vêm do que o PRMake REGISTROU (os PRs que ele abriu), não
/// de adivinhação no GitHub. No fluxo de produção os PRs saem de <c>hotfix/&lt;card&gt;-dev</c> e <c>-qa</c> (a principal é a
/// branch sem o sufixo, cortada da base do fluxo — a master); no fluxo de release o PR sai da própria principal para a
/// base escolhida (release-version, hotfix-version, edge…), e é esse destino que diz de onde ela saiu.
/// </summary>
public static class PromotionSource
{
    /// <summary>Sufixos das branches derivadas do <c>BranchStrategy</c> (<c>-dev</c>, <c>-qa</c>…), do mais longo para o mais curto.</summary>
    public static IReadOnlyList<string> Suffixes(string? branchStrategyJson)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(branchStrategyJson))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(branchStrategyJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("flows", out var flows) || flows.ValueKind != JsonValueKind.Object)
                return [];
            foreach (var flow in flows.EnumerateObject())
            {
                if (flow.Value.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var kind in flow.Value.EnumerateObject())
                {
                    if (kind.Value.ValueKind != JsonValueKind.Object || !kind.Value.TryGetProperty("prs", out var prs) || prs.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var pr in prs.EnumerateArray())
                        if (pr.TryGetProperty("suffix", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } suffix)
                            found.Add(suffix);
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }
        return found.OrderByDescending(x => x.Length).ToList();
    }

    /// <summary>"hotfix/77317-dev" → "hotfix/77317" (só o sufixo no fim do nome).</summary>
    public static string StripSuffix(string branch, IReadOnlyList<string> suffixes)
    {
        foreach (var suffix in suffixes)
            if (branch.Length > suffix.Length && branch.EndsWith(suffix, StringComparison.Ordinal))
                return branch[..^suffix.Length];
        return branch;
    }

    /// <summary>As branches de tratamento do card segundo o registro (sem sufixo, sem repetir), na ordem em que foram registradas.</summary>
    public static List<string> Principals(IEnumerable<RegisteredBranch> registered, IReadOnlyList<string> suffixes) =>
        registered.OrderBy(r => r.Id).Select(r => StripSuffix(r.Branch, suffixes)).Where(b => b.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// A principal que existe no remoto; havendo mais de uma, a que o padrão de nome do PRMake aponta (<c>hotfix/{card}</c>).
    /// Null = nenhuma das registradas existe no repositório.
    /// </summary>
    public static string? ChoosePrincipal(IReadOnlyList<string> principals, IReadOnlyCollection<string> remoteBranches, string? preferred)
    {
        var existing = principals.Where(p => remoteBranches.Contains(p, StringComparer.Ordinal)).ToList();
        if (existing.Count == 0)
            return null;
        return preferred is not null && existing.Contains(preferred, StringComparer.Ordinal) ? preferred : existing[0];
    }

    /// <summary>
    /// De onde a principal saiu. Fluxo de release: o destino do PR que sai da própria principal (o PRMake registrou a base
    /// escolhida; vale o registro mais antigo, anterior a qualquer promoção). Fluxo de produção: a base do fluxo no
    /// <c>BranchStrategy</c> (<c>base: master</c>).
    /// </summary>
    public static (string Base, string Reason) BaseOf(string principal, IEnumerable<RegisteredBranch> registered, string? kind, string? branchStrategyJson)
    {
        var own = registered.Where(r => string.Equals(r.Branch, principal, StringComparison.Ordinal)).OrderBy(r => r.Id).FirstOrDefault();
        if (own is not null && !string.IsNullOrWhiteSpace(own.Target))
            return (own.Target, $"o PRMake abriu o PR de {principal} para {own.Target}");
        return (StrategyBase(kind, branchStrategyJson), "base do fluxo de produção no BranchStrategy");
    }

    /// <summary>A <c>base</c> do fluxo "producao" para o tipo do repositório; sem configuração, master.</summary>
    public static string StrategyBase(string? kind, string? branchStrategyJson)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(branchStrategyJson))
            return "master";
        try
        {
            using var doc = JsonDocument.Parse(branchStrategyJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("flows", out var flows) && flows.ValueKind == JsonValueKind.Object
                && flows.TryGetProperty("producao", out var producao) && producao.ValueKind == JsonValueKind.Object
                && producao.TryGetProperty(kind, out var rule) && rule.ValueKind == JsonValueKind.Object
                && rule.TryGetProperty("base", out var b) && b.ValueKind == JsonValueKind.String && b.GetString() is { Length: > 0 } baseName)
                return baseName;
        }
        catch (JsonException)
        {
        }
        return "master";
    }
}
