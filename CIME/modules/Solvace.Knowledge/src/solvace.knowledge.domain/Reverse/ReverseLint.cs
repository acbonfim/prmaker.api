using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Reverse;

/// <summary>Resultado da checagem de um documento: erros barram o envio para revisão; avisos aparecem para o revisor.</summary>
public sealed class ReverseLintResult
{
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public int Items { get; set; }
    public Dictionary<string, int> ByKind { get; set; } = [];
    public List<string> MissingHeadings { get; set; } = [];
    /// <summary>Itens que exigem evidência (<c>**Onde:**</c>) e não têm.</summary>
    public List<string> WithoutEvidence { get; set; } = [];
    /// <summary>IDs publicados que sumiram do documento (devem ficar como "(removido)").</summary>
    public List<string> RemovedIds { get; set; } = [];
    /// <summary>Referências a itens do módulo que não existem em nenhum documento.</summary>
    public List<string> UnknownRefs { get; set; } = [];
    public double? Coverage { get; set; }
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Checagem estrutural de um documento da engenharia reversa (0052) — determinística, sem LLM: cabeçalhos obrigatórios,
/// itens com ID válido e único no módulo, evidência nos itens que exigem, IDs publicados que sumiram, referências
/// desconhecidas, segredo no texto e cobertura do inventário abaixo do mínimo.
/// </summary>
public static partial class ReverseLint
{
    /// <summary>Itens que precisam apontar o código (<c>**Onde:**</c> ou um arquivo:linha no bloco).</summary>
    public static readonly IReadOnlySet<string> NeedEvidence = new HashSet<string> { "RN", "UC", "API", "DB", "EVT", "JOB", "INT", "TELA" };

    /// <param name="publishedIds">IDs do documento publicado (para avisar o que sumiu).</param>
    /// <param name="otherDocIds">IDs definidos nos outros documentos publicados do módulo (ID → tipo do documento).</param>
    public static ReverseLintResult Run(ReverseDocType type, string content, IReadOnlyCollection<string>? publishedIds = null,
        IReadOnlyDictionary<string, string>? otherDocIds = null, double? coverage = null, double minCoverage = 0)
    {
        var result = new ReverseLintResult { Coverage = coverage };
        var text = content ?? string.Empty;
        if (text.Trim().Length == 0)
        {
            result.Errors.Add("Documento vazio.");
            return result;
        }

        var headings = ReverseDocParser.Headings(text);
        var h2 = headings.Where(h => h.Level == 2).Select(h => Normalize(h.Text)).ToList();
        foreach (var required in type.Headings)
            if (!h2.Any(h => h.Contains(required.Match, StringComparison.Ordinal)))
                result.MissingHeadings.Add(required.Title);
        if (result.MissingHeadings.Count > 0)
            result.Errors.Add($"Faltam seções obrigatórias (##): {string.Join(", ", result.MissingHeadings)}.");

        var items = ReverseDocParser.Parse(text);
        result.Items = items.Count(i => !i.Removed);
        result.ByKind = items.Where(i => !i.Removed).GroupBy(i => i.Kind).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
        if (items.Count == 0)
            result.Errors.Add($"Nenhum item com ID (ex.: `### {type.Kinds[0]}-001 — título`). O índice e as análises dependem dos itens.");

        foreach (var dup in items.GroupBy(i => i.Id).Where(g => g.Count() > 1))
            result.Errors.Add($"ID repetido no documento: {dup.Key} ({dup.Count()}×).");

        var foreign = items.Where(i => i.Kind != "GAP" && !type.Kinds.Contains(i.Kind)).Select(i => i.Id).Distinct().ToList();
        if (foreign.Count > 0)
            result.Warnings.Add($"Itens de outro documento definidos aqui (defina no documento certo e só referencie): {Join(foreign)}.");

        if (otherDocIds is not null)
        {
            var clash = items.Where(i => i.Kind != "GAP" && otherDocIds.ContainsKey(i.Id))
                .Select(i => $"{i.Id} (já em {otherDocIds[i.Id]})").ToList();
            if (clash.Count > 0)
                result.Errors.Add($"ID já usado em outro documento do módulo — o ID é único no módulo: {Join(clash)}.");
        }

        result.WithoutEvidence = items.Where(i => !i.Removed && NeedEvidence.Contains(i.Kind) && i.Evidence.Count == 0).Select(i => i.Id).ToList();
        if (result.WithoutEvidence.Count > 0)
            result.Warnings.Add($"{result.WithoutEvidence.Count} itens sem evidência no código (**Onde:** arquivo:linha): {Join(result.WithoutEvidence)}.");

        var integrationsWithoutModule = items.Where(i => !i.Removed && i.Kind == "INT" && i.Modules.Count == 0).Select(i => i.Id).ToList();
        if (integrationsWithoutModule.Count > 0)
            result.Warnings.Add($"Integrações sem **Módulos:** (o grafo entre módulos não as enxerga): {Join(integrationsWithoutModule)}.");

        if (publishedIds is { Count: > 0 })
        {
            var current = items.Select(i => i.Id).ToHashSet();
            result.RemovedIds = publishedIds.Where(id => !current.Contains(id)).OrderBy(id => id).ToList();
            if (result.RemovedIds.Count > 0)
                result.Warnings.Add($"IDs publicados que sumiram (análises antigas citam esses IDs — mantenha como `(removido)`): {Join(result.RemovedIds)}.");
        }

        var known = items.Select(i => i.Id).ToHashSet();
        if (otherDocIds is not null) known.UnionWith(otherDocIds.Keys);
        result.UnknownRefs = items.SelectMany(i => i.Refs).Where(r => !r.Contains('#') && !known.Contains(r)).Distinct().OrderBy(r => r).ToList();
        if (result.UnknownRefs.Count > 0)
            result.Warnings.Add($"Referências a itens que não existem no módulo (ainda): {Join(result.UnknownRefs)}.");

        var secret = SecretPattern().Match(text);
        if (secret.Success)
            result.Errors.Add("O texto parece conter credencial/segredo (senha, connection string, chave ou token) — só nomes de recursos e chaves.");

        if (coverage is { } c && minCoverage > 0 && c < minCoverage)
            result.Warnings.Add($"Cobertura do inventário {c:P0} abaixo do mínimo {minCoverage:P0} — itens do código sem menção no documento.");
        return result;
    }

    /// <summary>Sem acento e em minúsculas (para casar os cabeçalhos obrigatórios).</summary>
    public static string Normalize(string value)
    {
        var decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    private static string Join(List<string> values) =>
        values.Count <= 15 ? string.Join(", ", values) : string.Join(", ", values.Take(15)) + $" … (+{values.Count - 15})";

    [GeneratedRegex(@"(?ix)
        (?:password|pwd|secret|apikey|api_key)\s*=\s*[""']?[^\s""'`;,{}<>]{6,}
        | (?:server|host|data\ source)=[^;\n]+;[^\n]*(?:password|pwd)=
        | \bAKIA[0-9A-Z]{16}\b
        | -----BEGIN\ [A-Z ]*PRIVATE\ KEY-----
        | \beyJ[\w-]{10,}\.[\w-]{10,}\.[\w-]{10,}")]
    private static partial Regex SecretPattern();
}
