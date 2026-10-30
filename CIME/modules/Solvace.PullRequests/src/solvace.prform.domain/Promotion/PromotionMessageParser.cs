using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace solvace.prform.domain.Promotion;

public record ParsedCard(string Card, string WorkItemType, string Title, string? AssignedTo);

/// <summary>Regra de destino lida da mensagem: o tipo de ticket como o Release Manager escreveu e a que tipo de repositório (kind) ela vale.</summary>
public record ParsedRule(string TicketType, string? Kind, List<PromotionTarget> Targets);

/// <summary>
/// 0074: parte determinística da leitura da mensagem do Release Manager — os cards saem por regex (sem IA, sem custo);
/// a IA só interpreta o texto livre (versão e regras de destino) e a resposta dela é validada aqui antes de virar campo.
/// </summary>
public static class PromotionMessageParser
{
    private static readonly Regex CardLine = new(
        @"^\s*(?<id>\d{3,8})[\t ]+(?<type>Bug|User Story|Task|Product Backlog Item|Feature|Epic|Issue|Impediment|Test Case)\b[\t ]*(?<title>.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex HeaderLine = new(
        @"^\s*ID\s+Work Item Type\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex KindAndVersion = new(
        @"\b(?<kind>hotfix|release)\b\W{0,12}[vV]?(?<ver>\d+\.\d+(?:\.\d+){0,2}(?:[-.][0-9A-Za-z]+)*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AnyVersion = new(@"\b[vV](?<ver>\d+\.\d+(?:\.\d+){1,2})\b", RegexOptions.Compiled);

    private static readonly Regex RuleLine = new(
        @"^\s*(?:branch(?:es)?\s+(?:para|for)\s+)(?:tickets?\s+)?(?:com\s+|with\s+)?(?<label>[^:]{2,80}):\s*(?<list>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase) { "de", "da", "do", "das", "dos", "api", "the", "and", "para", "com" };

    private static readonly Regex BranchToken = new(@"^[A-Za-z0-9._{}/-]{1,100}$", RegexOptions.Compiled);

    /// <summary>
    /// Cards da tabela colada (<c>ID  Work Item Type  Title  Assigned To</c>, separados por tab ou espaços). O responsável
    /// vem na linha seguinte ao card, quando ela não é outro card. Repetidos são ignorados.
    /// </summary>
    public static List<ParsedCard> ExtractCards(string? text)
    {
        var cards = new List<ParsedCard>();
        if (string.IsNullOrWhiteSpace(text))
            return cards;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var seen = new HashSet<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var m = CardLine.Match(lines[i]);
            if (!m.Success || !seen.Add(m.Groups["id"].Value))
                continue;

            // "Title<TAB>Assigned To" na mesma linha (quando colado de uma planilha); senão, a linha seguinte.
            var title = m.Groups["title"].Value;
            string? assigned = null;
            var tab = title.LastIndexOf('\t');
            if (tab > 0)
            {
                assigned = title[(tab + 1)..].Trim();
                title = title[..tab].Trim();
            }
            else if (i + 1 < lines.Length)
            {
                var next = lines[i + 1].Trim();
                if (next.Length is > 0 and <= 120 && !CardLine.IsMatch(lines[i + 1]) && !HeaderLine.IsMatch(next))
                    assigned = next;
            }
            cards.Add(new ParsedCard(m.Groups["id"].Value, Capitalize(m.Groups["type"].Value), title.Trim(), string.IsNullOrWhiteSpace(assigned) ? null : assigned));
        }
        return cards;
    }

    /// <summary>O texto sem a tabela de cards — é o que a IA lê (menos tokens, e nada do conteúdo dos cards vai junto).</summary>
    public static string StripCardTable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        var inTable = false;
        foreach (var line in lines)
        {
            if (HeaderLine.IsMatch(line)) { inTable = true; continue; }
            if (CardLine.IsMatch(line)) { inTable = true; continue; }
            if (inTable)
            {
                // Dentro da tabela: linha curta sem ":" é o responsável do card anterior.
                var t = line.Trim();
                if (t.Length == 0) { inTable = false; kept.Add(line); continue; }
                if (t.Length <= 120 && !t.Contains(':')) continue;
                inTable = false;
            }
            kept.Add(line);
        }
        return string.Join('\n', kept).Trim();
    }

    /// <summary>"hotfix V3.274.10" → (hotfix, 3.274.10). Sem "hotfix/release" por perto, aceita um "V3.274.10" solto.</summary>
    public static (string? Kind, string? Version) ExtractKindAndVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null);
        var m = KindAndVersion.Match(text);
        if (m.Success)
            return (m.Groups["kind"].Value.ToLowerInvariant(), PromotionTargetResolver.NormalizeVersion(m.Groups["ver"].Value));
        var any = AnyVersion.Match(text);
        return (null, any.Success ? PromotionTargetResolver.NormalizeVersion(any.Groups["ver"].Value) : null);
    }

    /// <summary>
    /// Regras escritas como "Branch para ticket Netcore: hotfix-version" — sem IA, para quando ela não está disponível
    /// (e para conferir o que ela devolve). O <c>Kind</c> vem nulo: quem sabe a que repositório o rótulo se refere é a
    /// configuração de perfis ou a IA.
    /// </summary>
    public static List<ParsedRule> ExtractRules(string? text)
    {
        var rules = new List<ParsedRule>();
        foreach (var raw in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var m = RuleLine.Match(raw);
            if (!m.Success)
                continue;
            var targets = ParseBranchList(m.Groups["list"].Value);
            if (targets.Count > 0)
                rules.Add(new ParsedRule(m.Groups["label"].Value.Trim(), null, targets));
        }
        return rules;
    }

    /// <summary>"release-version, edge, master (draft)" / "release-version e master" → destinos. Máximo de 8.</summary>
    public static List<PromotionTarget> ParseBranchList(string? list)
    {
        var targets = new List<PromotionTarget>();
        if (string.IsNullOrWhiteSpace(list))
            return targets;

        var parts = Regex.Split(list.Trim().TrimEnd('.', ';'), @"\s*(?:,|;|\be\b|\band\b|\+|/\s)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var part in parts)
        {
            var token = part.Trim();
            var draft = false;
            var paren = Regex.Match(token, @"\((?<note>[^)]*)\)\s*$");
            if (paren.Success)
            {
                draft = Regex.IsMatch(paren.Groups["note"].Value, @"draft|rascunho", RegexOptions.IgnoreCase);
                token = token[..paren.Index].Trim();
            }
            var template = NormalizeTemplate(token);
            if (template is null || targets.Any(t => string.Equals(t.Branch, template, StringComparison.OrdinalIgnoreCase)))
                continue;
            targets.Add(new PromotionTarget(template, draft));
            if (targets.Count == 8)
                break;
        }
        return targets;
    }

    /// <summary>
    /// O nome como o Release Manager escreveu ("release-version", "hotfix-version", "edge", "master" são branches reais do
    /// repositório — nada é trocado por número de versão aqui); nome com caractere estranho → null.
    /// </summary>
    public static string? NormalizeTemplate(string? token)
    {
        var t = token?.Trim().Trim('`', '"', '\'');
        if (string.IsNullOrEmpty(t))
            return null;
        return BranchToken.IsMatch(t) && !t.Contains("..") && !t.StartsWith('/') && !t.EndsWith('/') ? t : null;
    }

    /// <summary>
    /// Valida a resposta JSON da IA ({"version", "kind", "rules": [{"ticketType", "kind", "targets": [{"branch", "draft"}]}]}):
    /// aceita só tipos de repositório conhecidos e nomes de branch bem formados. Lixo vira aviso, nunca campo.
    /// </summary>
    public static (string? Version, string? Kind, List<ParsedRule> Rules, List<string> Warnings) ParseAiJson(string? content, IReadOnlyCollection<string> knownKinds)
    {
        var warnings = new List<string>();
        var rules = new List<ParsedRule>();
        var json = ExtractJsonObject(content);
        if (json is null)
        {
            warnings.Add("A IA não devolveu um JSON válido.");
            return (null, null, rules, warnings);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var version = Str(root, "version") is { } v ? PromotionTargetResolver.NormalizeVersion(v) : null;
            var kindRaw = Str(root, "kind")?.ToLowerInvariant();
            var kind = kindRaw is "hotfix" or "release" ? kindRaw : null;

            if (root.TryGetProperty("rules", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in list.EnumerateArray())
                {
                    var label = Str(r, "ticketType") ?? Str(r, "label") ?? "?";
                    var ruleKind = Str(r, "kind");
                    var matched = knownKinds.FirstOrDefault(x => string.Equals(x, ruleKind, StringComparison.OrdinalIgnoreCase));
                    if (matched is null)
                    {
                        warnings.Add($"Regra \"{label}\": tipo de repositório \"{ruleKind ?? "—"}\" desconhecido — escolha os destinos na tela.");
                        continue;
                    }
                    var targets = new List<PromotionTarget>();
                    if (r.TryGetProperty("targets", out var ts) && ts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in ts.EnumerateArray())
                        {
                            var template = NormalizeTemplate(Str(t, "branch"));
                            if (template is null)
                            {
                                warnings.Add($"Regra \"{label}\": destino ignorado (nome inválido).");
                                continue;
                            }
                            var draft = t.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True;
                            if (targets.All(x => !string.Equals(x.Branch, template, StringComparison.OrdinalIgnoreCase)) && targets.Count < 8)
                                targets.Add(new PromotionTarget(template, draft));
                        }
                    }
                    if (targets.Count > 0)
                        rules.Add(new ParsedRule(label, matched, targets));
                }
            }
            return (version, kind, rules, warnings);
        }
        catch (JsonException)
        {
            warnings.Add("A IA não devolveu um JSON válido.");
            return (null, null, rules, warnings);
        }
    }

    private static string? ExtractJsonObject(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    private static string? Str(JsonElement e, string name)
    {
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
        return null;
    }

    /// <summary>Rótulo do ticket ("API De Integrações") → tipo de repositório, pelas palavras em comum com o rótulo/chave dos perfis do PRMake.</summary>
    public static string? MatchKind(string ticketType, PromotionProfileSet profiles)
    {
        var words = Words(ticketType);
        if (words.Count == 0)
            return null;
        var scored = profiles.All
            .Select(p => (p.Key, Score: Words(p.Label + " " + p.Key).Count(w => words.Contains(w))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();
        if (scored.Count == 0)
            return null;
        // Empate no topo = ambíguo: deixa para o usuário.
        return scored.Count > 1 && scored[0].Score == scored[1].Score ? null : scored[0].Key;
    }

    private static HashSet<string> Words(string text)
    {
        var normalized = new string(text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        return normalized.Split([' ', '(', ')', '-', '/', ',', '.', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Select(w => w.StartsWith("integra") ? "integra" : w)
            .ToHashSet();
    }

    private static string Capitalize(string s) =>
        string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
}
