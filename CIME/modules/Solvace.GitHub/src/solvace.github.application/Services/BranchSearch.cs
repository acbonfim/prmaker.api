using solvace.github.domain.Responses;

namespace solvace.github.application.Services;

/// <summary>
/// 0085 r3: filtro e ordem das branches do autocomplete — contém <c>search</c> (sem diferenciar maiúsculas), na ordem:
/// a padrão do repositório; as bases do fluxo (na ordem informada) e as protegidas; as do card (começam pela branch
/// principal do card, ex. <c>hotfix/77434</c>, <c>hotfix/77434-rv</c>, ou têm o número do card); o resto em ordem alfabética.
/// </summary>
public static class BranchSearch
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    /// <param name="branches">Todas as branches do repositório.</param>
    /// <param name="search">Trecho do nome (vazio = todas).</param>
    /// <param name="limit">Máximo devolvido (1–200; inválido = 50).</param>
    /// <param name="flowBases">Bases/destinos do fluxo (master, development, qa, release-version...), na ordem de prioridade.</param>
    /// <param name="cardBranch">Branch principal do card (ex.: hotfix/77434) — as que começam por ela vêm antes do resto.</param>
    /// <param name="card">Número do card — branches com ele no nome também contam como do card.</param>
    public static List<BranchInfoResponse> Rank(IEnumerable<BranchInfoResponse> branches, string? search, int? limit,
        IReadOnlyList<string>? flowBases = null, string? cardBranch = null, string? card = null)
    {
        var take = limit is > 0 ? Math.Min(limit.Value, MaxLimit) : DefaultLimit;
        var q = search?.Trim() ?? string.Empty;
        var bases = flowBases ?? [];
        var main = cardBranch?.Trim() ?? string.Empty;
        var number = card?.Trim() ?? string.Empty;

        int BaseIndex(string name)
        {
            for (var i = 0; i < bases.Count; i++)
                if (string.Equals(bases[i]?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return int.MaxValue;
        }

        bool IsCardBranch(string name) =>
            (main.Length > 0 && name.StartsWith(main, StringComparison.OrdinalIgnoreCase))
            || (number.Length > 0 && ContainsNumber(name, number));

        int Group(BranchInfoResponse b) =>
            b.IsDefault ? 0
            : BaseIndex(b.Name) != int.MaxValue || b.Protected ? 1
            : IsCardBranch(b.Name) ? 2
            : 3;

        return branches
            .Where(b => !string.IsNullOrWhiteSpace(b.Name))
            .Where(b => q.Length == 0 || b.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Group)
            .ThenBy(b => BaseIndex(b.Name))
            .ThenBy(b => main.Length > 0 && string.Equals(b.Name, main, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    /// <summary>O número aparece inteiro no nome (77434 casa com hotfix/77434-rv, não com hotfix/177434).</summary>
    private static bool ContainsNumber(string name, string number)
    {
        var at = 0;
        while ((at = name.IndexOf(number, at, StringComparison.Ordinal)) >= 0)
        {
            var before = at == 0 || !char.IsDigit(name[at - 1]);
            var end = at + number.Length;
            var after = end >= name.Length || !char.IsDigit(name[end]);
            if (before && after) return true;
            at = end;
        }
        return false;
    }
}
