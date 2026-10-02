namespace solvace.prform.AiUsage;

/// <summary>Ações de IA conhecidas (0042): chave gravada no consumo e o nome que a tela mostra.</summary>
public static class AiUsageActions
{
    private static readonly (string Route, string Action)[] Routes =
    [
        ("Architecture/ask/deep", "base-solvace:deep"),
        ("Architecture/ask", "base-solvace:ask"),
        ("Architecture/projects/{key}/guide", "base-solvace:guide"),
        ("Architecture/learn-from-card", "base-solvace:learn"),
        ("Architecture/projects/{key}/sections/{section}/chat", "base-solvace:chat"),
        ("AI/generate", "ai:generate"),
    ];

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["base-solvace:ask"] = "Pergunte à Base Solvace",
        ["base-solvace:deep"] = "Analisar a fundo (Base Solvace)",
        ["base-solvace:guide"] = "Gerar guia com a IA (Base Solvace)",
        ["base-solvace:learn"] = "Aprender com um card",
        ["base-solvace:chat"] = "Sugerir melhoria de seção (Base Solvace)",
        ["ai:generate"] = "Gerar com IA",
        ["pr:generate"] = "Gerar descrição/RCA do PR",
        ["handover:generate"] = "Gerar passagem de conhecimento",
        ["devops:summary"] = "Resumo não técnico (Ações DevOps)",
    };

    public static string FromRoute(string route)
    {
        var r = (route ?? string.Empty).Trim('/');
        foreach (var (known, action) in Routes)
            if (r.Equals(known, StringComparison.OrdinalIgnoreCase)) return action;
        return r.Length == 0 ? "desconhecida" : r;
    }

    public static string Label(string action) => Labels.TryGetValue(action, out var label) ? label : action;
}
