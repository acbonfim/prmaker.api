using solvace.azure.domain.Models;

namespace solvace.azure.application.Services;

/// <summary>
/// 0085 r3: monta a lista de estados do card para o editor do item de trabalho (mover o card) e marca os próximos
/// usuais. Primeiro as ações configuradas (Dev Test in QA → Ready for QA → Test in production, nessa ordem): a partir
/// do estado atual, só as que vêm depois dele. Sem nenhuma ação aplicável (US, ações não configuradas, card já no fim
/// das ações), o próximo estado na ordem do DevOps e o primeiro da categoria seguinte (Proposed → InProgress →
/// Resolved → Completed; Removed nunca é sugerido).
/// </summary>
public static class CardStateSuggestions
{
    public const string DevTestInQa = "dev-test-in-qa";
    public const string ReadyForQa = "ready-for-qa";
    public const string TestInProduction = "test-in-production";

    private static readonly string[] CategoryOrder = ["Proposed", "InProgress", "Resolved", "Completed"];

    /// <param name="states">Estados válidos do tipo, na ordem do DevOps.</param>
    /// <param name="current">Estado atual do card.</param>
    /// <param name="actions">Ações configuradas, na ordem do fluxo (ação, estado de destino).</param>
    public static List<CardStateOption> Build(IReadOnlyList<WorkItemTypeState> states, string? current,
        IReadOnlyList<(string Action, string? State)> actions)
    {
        var options = states
            .Select(s => new CardStateOption
            {
                Name = s.Name,
                Category = string.IsNullOrWhiteSpace(s.Category) ? null : s.Category,
                Current = Same(s.Name, current)
            })
            .ToList();

        // Ações cujo estado existe no tipo do card, na ordem do fluxo.
        var valid = actions
            .Select(a => (a.Action, Option: options.FirstOrDefault(o => Same(o.Name, a.State?.Trim()))))
            .Where(a => a.Option is not null)
            .ToList();
        var at = valid.FindLastIndex(a => a.Option!.Current);
        foreach (var (action, option) in valid.Skip(at + 1))
        {
            if (option!.Current) continue;
            option.Suggested = true;
            option.Action ??= action;
        }
        if (options.Any(o => o.Suggested))
            return options;

        // Reserva: próximos pela ordem e pela categoria.
        var index = options.FindIndex(o => o.Current);
        if (index >= 0 && index + 1 < options.Count && Rank(options[index + 1].Category) >= 0)
            options[index + 1].Suggested = true;
        var currentRank = index >= 0 ? Rank(options[index].Category) : -1;
        var nextCategory = options
            .Where(o => !o.Current && Rank(o.Category) > currentRank)
            .OrderBy(o => Rank(o.Category))
            .FirstOrDefault();
        if (nextCategory is not null)
            nextCategory.Suggested = true;
        return options;
    }

    private static int Rank(string? category) =>
        category is null ? -1 : Array.FindIndex(CategoryOrder, c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
