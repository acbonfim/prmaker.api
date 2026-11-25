using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0085 r5 (1.5): terminologia por tipo de card. O plano da fase <c>correction</c> de uma User Story (Kind
/// <c>analisar-us</c>) é o <b>plano de implementação</b> — nunca "correção", "causa raiz" ou "RCA"; o do Bug segue "plano de
/// correção". Usado nos textos da Timeline, no contexto do especialista e no título padrão do plano.
/// </summary>
public static partial class PlanTerms
{
    /// <summary>0096: começo do motivo de cancelamento da versão de entrega substituída por uma versão nova.</summary>
    public const string SupersededReason = "Substituída pela versão nova";

    /// <summary>0096: o plano foi encerrado por ter sido substituído (não pela tela) — o tratamento que o criou segue.</summary>
    public static bool IsSuperseded(string? statusReason) =>
        statusReason?.StartsWith(SupersededReason, StringComparison.Ordinal) == true;

    /// <summary>"implementação" (US) ou "correção" (Bug).</summary>
    public static string Delivery(string? planKind) => WorkItem.IsUserStoryPlan(planKind) ? "implementação" : "correção";

    /// <summary>"Implementação" ou "Correção".</summary>
    public static string DeliveryTitle(string? planKind) => WorkItem.IsUserStoryPlan(planKind) ? "Implementação" : "Correção";

    /// <summary>"plano de implementação" ou "plano de correção".</summary>
    public static string DeliveryPlan(string? planKind) => $"plano de {Delivery(planKind)}";

    /// <summary>"Plano de implementação" ou "Plano de correção".</summary>
    public static string DeliveryPlanTitle(string? planKind) => $"Plano de {Delivery(planKind)}";

    /// <summary>
    /// Título do plano da fase de entrega: na US, "Implementação &lt;card&gt;: ..." (tira "Correção/Plano de correção/Corrigir
    /// [card]" do começo); no Bug, como veio.
    /// </summary>
    public static string? CorrectionTitle(string? planKind, string card, string? title)
    {
        if (!WorkItem.IsUserStoryPlan(planKind)) return title;
        var t = (title ?? string.Empty).Trim();
        if (t.StartsWith("Implementação", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Implementacao", StringComparison.OrdinalIgnoreCase))
            return t;
        var rest = BugPrefix().Replace(t, string.Empty, 1).Trim();
        var c = (card ?? string.Empty).Trim().TrimStart('#');
        if (c.Length > 0)
            rest = Regex.Replace(rest, $@"^(do\s+card\s+|da\s+US\s+|card\s+)?#?{Regex.Escape(c)}\b\s*[:\-–—]?\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
        var head = c.Length > 0 ? $"Implementação {c}" : "Implementação";
        return rest.Length == 0 ? head : $"{head}: {rest}";
    }

    [GeneratedRegex(@"^(plano\s+de\s+)?(corre[çc][ãa]o|corrigir|fix|bugfix|hotfix)\b\s*[:\-–—]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex BugPrefix();
}
