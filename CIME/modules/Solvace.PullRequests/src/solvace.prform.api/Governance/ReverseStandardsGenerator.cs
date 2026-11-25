using System.Text.RegularExpressions;
using solvace.governance.domain;
using solvace.governance.domain.Entities;
using solvace.governance.domain.Standards;

namespace solvace.prform.Governance;

/// <summary>Item publicado da engenharia reversa que o gerador lê (projeção do índice).</summary>
public sealed record ReverseStandardSource(string Module, string ItemId, string Kind, string Title, string Body, IReadOnlyList<string> Evidence);

/// <summary>
/// 0101: padrões de estrutura de aplicação a partir da engenharia reversa publicada — sem IA e sem copiar o texto:
/// cada decisão de arquitetura (ADR) vira um padrão do módulo; as tecnologias (TEC) viram o padrão de stack; e o
/// <c>Onde:</c> dos itens do índice técnico (END endpoints, ROT rotas, TAB tabelas, DBO objetos de banco) diz onde cada
/// coisa fica no repositório. Tudo referencia o item (<c>módulo#ID</c>), entra vigente e em monitorar.
/// </summary>
public static partial class ReverseStandardsGenerator
{
    public static readonly string[] Kinds = ["ADR", "TEC", "END", "ROT", "TAB", "DBO"];

    private static readonly Dictionary<string, (string Topic, string What)> Structure = new()
    {
        ["END"] = ("api", "Endpoints HTTP"),
        ["ROT"] = ("folders", "Rotas e telas do front"),
        ["TAB"] = ("data-access", "Mapeamento de tabelas"),
        ["DBO"] = ("data-access", "Objetos de banco (procedures, views, functions)")
    };

    [GeneratedRegex(@"^(?<repo>[\w.-]+)/(?<path>[^:\s`]+?)(?::\d+)?$")]
    private static partial Regex RepoPath();

    public static List<ParsedStandard> Generate(IEnumerable<ReverseStandardSource> items)
    {
        var result = new List<ParsedStandard>();
        var order = 10_000;
        foreach (var module in items.GroupBy(i => i.Module.ToLowerInvariant()).OrderBy(g => g.Key))
        {
            var repos = module.SelectMany(i => i.Evidence).Select(Location).OfType<(string Repo, string Path)>()
                .GroupBy(l => l.Repo).OrderByDescending(g => g.Count()).Select(g => g.Key.ToLowerInvariant()).Take(5).ToList();
            var scope = new StandardScope { Modules = [module.Key], Repositories = repos };

            foreach (var adr in module.Where(i => i.Kind == "ADR").OrderBy(i => i.ItemId))
            {
                var decision = Decision(adr.Body);
                result.Add(Make(module.Key, adr.ItemId.ToLowerInvariant(), adr.ItemId, "decision", adr.Title,
                    $"{decision} (decisão de arquitetura observada no módulo {module.Key} — ver {module.Key}#{adr.ItemId}).",
                    scope, $"{module.Key}#{adr.ItemId}", adr.Title + "\n" + decision, ++order));
            }

            var tecs = module.Where(i => i.Kind == "TEC").OrderBy(i => i.ItemId).ToList();
            if (tecs.Count > 0)
                result.Add(Make(module.Key, "stack", "STACK", "stack", $"Stack do módulo {module.Key}",
                    "Use as tecnologias que o módulo já usa; tecnologia nova é decisão de arquitetura (combine antes): "
                    + string.Join("; ", tecs.Select(t => $"{t.Title} ({t.ItemId})")) + ".",
                    scope, $"{module.Key}#" + string.Join(",", tecs.Select(t => t.ItemId)), string.Join("|", tecs.Select(t => t.Title)), ++order));

            foreach (var (kind, (topic, what)) in Structure)
            {
                var places = module.Where(i => i.Kind == kind).SelectMany(i => i.Evidence).Select(Location).OfType<(string Repo, string Path)>().ToList();
                if (places.Count < 3) continue;
                var dirs = places.GroupBy(p => $"{p.Repo}/{Directory(p.Path)}").OrderByDescending(g => g.Count()).Take(3)
                    .Select(g => (Dir: g.Key, Count: g.Count())).ToList();
                var text = $"{what} do módulo ficam em " + string.Join(", ", dirs.Select(d => $"`{d.Dir}/` ({d.Count} de {places.Count})"))
                           + ". Novos seguem o mesmo lugar e o mesmo padrão de nome dos vizinhos.";
                result.Add(Make(module.Key, "where-" + kind.ToLowerInvariant(), "ONDE-" + kind, topic, $"{what}: onde ficam ({module.Key})",
                    text, scope, $"{module.Key}#{kind}", string.Join("|", dirs.Select(d => d.Dir)), ++order));
            }
        }
        return result;
    }

    private static ParsedStandard Make(string module, string suffix, string code, string topic, string title, string text, StandardScope scope,
        string reference, string hashText, int order)
    {
        var key = Key(module, suffix);
        var hash = WikiStandardsParser.Hash(text + "\n" + hashText);
        return new ParsedStandard(key, new StandardData
        {
            Code = code.Length <= 20 ? code : code[..20],
            Area = StandardArea.Application,
            Topic = topic,
            Title = title,
            TextPt = text,
            AppliesTo = scope,
            Source = new StandardSource { Kind = "reverse-engineering", Ref = reference, Hash = hash },
            Status = StandardStatus.Active,
            Effect = GovernanceEffect.Monitor,
            Check = new StandardCheck(),
            Order = order
        }, false, hash);
    }

    /// <summary>Chave estável e válida (≤ 80, minúsculas/números/hífen).</summary>
    public static string Key(string module, string suffix)
    {
        var raw = Regex.Replace($"app-{module}-{suffix}".ToLowerInvariant(), "[^a-z0-9-]+", "-").Trim('-');
        return raw.Length <= 80 ? raw : raw[..71] + "-" + WikiStandardsParser.Hash(raw)[..8];
    }

    /// <summary>"Decisão:" do corpo da ADR (ou a primeira frase útil).</summary>
    private static string Decision(string body)
    {
        var m = Regex.Match(body, @"\*\*Decis[aã]o:?\*\*:?\s*(?<d>[^\n]+)", RegexOptions.IgnoreCase);
        var text = m.Success ? m.Groups["d"].Value : body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 20 && !l.StartsWith("- **") && !l.StartsWith('#')) ?? body;
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= 600 ? text : text[..597] + "…";
    }

    /// <summary>"repo/caminho/arquivo.ext:42" → (repo, caminho/arquivo.ext); banco/aws/sem repositório → null.</summary>
    public static (string Repo, string Path)? Location(string evidence)
    {
        var v = evidence.Trim().Trim('`').Trim();
        if (v.StartsWith("banco", StringComparison.OrdinalIgnoreCase) || v.StartsWith("aws", StringComparison.OrdinalIgnoreCase)) return null;
        var m = RepoPath().Match(v);
        return m.Success && m.Groups["path"].Value.Contains('/') ? (m.Groups["repo"].Value, m.Groups["path"].Value) : null;
    }

    private static string Directory(string path)
    {
        var i = path.LastIndexOf('/');
        return i > 0 ? path[..i] : path;
    }
}
