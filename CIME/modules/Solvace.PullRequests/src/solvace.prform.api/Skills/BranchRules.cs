using System.Text.Json;
using System.Text.RegularExpressions;

namespace solvace.prform.Skills;

/// <summary>
/// 0085: nomes de branch por destino — <c>{branch}{suffix}</c> com o sufixo do <c>BranchSuffixByTarget</c>
/// (<c>release-version</c> → <c>-rv</c>); destino sem sufixo configurado vira <c>-&lt;destino&gt;</c>. Usado pela promoção
/// em lote, pelas regras do card (<c>GET Skills/rules</c>) e pelos modelos de itens de trabalho — nada fixo no código.
/// </summary>
public static partial class BranchRules
{
    /// <summary>Padrão quando a chave não existe (o mesmo que a migração SeedWorkItems0085 semeia).</summary>
    public const string DefaultSuffixesJson =
        """{"development":"-dev","qa":"-qa","release-version":"-rv","hotfix-version":"-hv","edge":"-edge"}""";

    /// <summary><c>BranchSuffixByTarget</c> (JSON objeto destino → sufixo); inválido/vazio → o padrão.</summary>
    public static IReadOnlyDictionary<string, string> ParseSuffixes(string? json)
    {
        var parsed = TryParse(json);
        return parsed.Count > 0 ? parsed : TryParse(DefaultSuffixesJson);
    }

    private static Dictionary<string, string> TryParse(string? json)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && p.Name.Trim().Length > 0)
                    map[p.Name.Trim()] = p.Value.GetString()!.Trim();
        }
        catch (JsonException)
        {
        }
        return map;
    }

    /// <summary>Destino seguro para nome de ref ("/" e espaços viram "-").</summary>
    public static string SafeTarget(string target) => UnsafeChars().Replace(target.Trim(), "-").Trim('-');

    /// <summary>Sufixo do destino: o configurado, senão <c>-&lt;destino&gt;</c>.</summary>
    public static string SuffixFor(string target, IReadOnlyDictionary<string, string> suffixes) =>
        suffixes.TryGetValue(target.Trim(), out var s) ? s : "-" + SafeTarget(target);

    /// <summary>Branch de trabalho para o destino: <c>hotfix/77434</c> + <c>-rv</c>.</summary>
    public static string BranchFor(string mainBranch, string target, IReadOnlyDictionary<string, string> suffixes) =>
        mainBranch + SuffixFor(target, suffixes);

    /// <summary>
    /// r5: confere a branch de origem de um PR pelas regras do card. Erro (não abre): sem prefixo ("hotfix/...") ou sem o
    /// número do card. Aviso (abre): fora do <c>BranchNamePattern</c>. As mensagens citam o nome esperado para o destino
    /// (<c>{branch}{suffix}</c>, ex.: <c>hotfix/77434-rv</c>); promoção (<c>{branch}-...</c>) e a principal passam sem aviso.
    /// </summary>
    public static (string? Error, string? Warning) CheckPrSource(string source, string card, string mainBranch, string target,
        IReadOnlyDictionary<string, string> suffixes)
    {
        var s = (source ?? string.Empty).Trim();
        var expected = BranchFor(mainBranch, target, suffixes);
        if (!s.Contains('/'))
            return ($"Informe a branch completa (ex.: {expected}).", null);
        if (!s.Contains(card, StringComparison.OrdinalIgnoreCase))
            return ($"A branch {s} nao e deste card ({card}) — pelas regras o nome e {expected} (confira com prmake_rules; nunca invente nome).", null);
        if (!s.StartsWith(mainBranch, StringComparison.OrdinalIgnoreCase))
            return (null, $"A branch {s} esta fora do padrao do card ({mainBranch}...); pelas regras para {target}: {expected}.");
        return (null, null);
    }

    /// <summary>Padrão da branch principal de Bug quando <c>BranchNamePattern</c> não existe.</summary>
    public const string DefaultBugPattern = "hotfix/{card}";

    /// <summary>Padrão da branch principal de User Story quando <c>BranchNamePatternUserStory</c> não existe.</summary>
    public const string DefaultUserStoryPattern = "feature/{card}";

    /// <summary>
    /// Padrão da branch principal pelo tipo do card: Bug → <c>BranchNamePattern</c> (<c>hotfix/{card}</c>); User Story →
    /// <c>BranchNamePatternUserStory</c> (<c>feature/{card}</c>) — User Story nunca recebe prefixo de hotfix.
    /// </summary>
    public static string PatternFor(bool isUserStory, string? bugPattern, string? userStoryPattern)
    {
        var chosen = isUserStory ? userStoryPattern : bugPattern;
        return string.IsNullOrWhiteSpace(chosen) ? (isUserStory ? DefaultUserStoryPattern : DefaultBugPattern) : chosen.Trim();
    }

    /// <summary>Branch principal do card pelo padrão do tipo (<c>hotfix/{card}</c> no Bug, <c>feature/{card}</c> na US).</summary>
    public static string MainBranch(string? pattern, string card) =>
        (string.IsNullOrWhiteSpace(pattern) ? "hotfix/{card}" : pattern.Trim()).Replace("{card}", card, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Troca os marcadores de um texto de modelo: <c>{card}</c>, <c>{branch}</c>, <c>{suffix:&lt;destino&gt;}</c> e os
    /// extras informados (<c>{repository}</c>, <c>{flowTargets}</c>…). Marcador desconhecido fica como está.
    /// </summary>
    public static string Expand(string? text, string card, string mainBranch, IReadOnlyDictionary<string, string> suffixes,
        IReadOnlyDictionary<string, string>? extras = null)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var result = SuffixMarker().Replace(text, m => SuffixFor(m.Groups[1].Value, suffixes));
        result = result.Replace("{card}", card, StringComparison.OrdinalIgnoreCase)
            .Replace("{branch}", mainBranch, StringComparison.OrdinalIgnoreCase);
        if (extras is not null)
            foreach (var (key, value) in extras)
                result = result.Replace("{" + key + "}", value, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex UnsafeChars();

    [GeneratedRegex(@"\{suffix:([^}]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex SuffixMarker();
}
