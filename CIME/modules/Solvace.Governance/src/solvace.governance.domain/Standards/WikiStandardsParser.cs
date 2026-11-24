using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using solvace.governance.domain.Entities;

namespace solvace.governance.domain.Standards;

/// <summary>Uma página de wiki lida (markdown do Azure DevOps).</summary>
public sealed record WikiPage(string Path, string Markdown, string? Url);

/// <summary>Um padrão extraído da wiki (vira rascunho de importação).</summary>
public sealed record ParsedStandard(string Key, StandardData Data, bool IsNew, string Hash);

/// <summary>
/// 0101: lê a wiki "Data Nomenclature and Rules" (página principal + subpáginas por objeto) e extrai cada regra com ID
/// das tabelas <c>| ID | English | Português | Exemplo |</c>. (NEW) = proposta que o DBA ainda valida → estado proposto;
/// (CHANGED) e as demais → vigente. Todas em monitorar. Regras que dá para conferir no texto de um script/migração ganham
/// a conferência embutida correspondente (<see cref="SqlStandardChecks"/>).
/// </summary>
public static partial class WikiStandardsParser
{
    [GeneratedRegex(@"^\|\s*(?<id>[A-Z]{1,4}\d{1,2})(?<tag>\s*\*\*\((?:NEW|CHANGED)\)\*\*)?\s*\|(?<rest>.*)\|\s*$")]
    private static partial Regex RuleRow();

    [GeneratedRegex(@"\*\*Pattern / Padrão:\*\*\s*`(?<p>[^`]+)`")]
    private static partial Regex PagePattern();

    [GeneratedRegex(@"\*\*Status\*\*\s*\|\s*(?<v>v[\d.]+)")]
    private static partial Regex VersionRow();

    [GeneratedRegex(@"```sql\s*\n(?<sql>.*?)```", RegexOptions.Singleline)]
    private static partial Regex SqlBlock();

    [GeneratedRegex(@"\[(?<t>[^\]]+)\]\([^)]+\)")]
    private static partial Regex Link();

    /// <summary>Prefixo do ID → assunto.</summary>
    private static readonly (string Prefix, string Topic)[] Topics =
    [
        ("IXU", "unique-index"), ("CK", "check"), ("DF", "default"), ("FK", "fk"), ("IX", "index"), ("PK", "pk"),
        ("G", "general"), ("N", "naming"), ("C", "query"), ("S", "schema"), ("R", "design"), ("P", "procedure"),
        ("T", "table"), ("V", "view")
    ];

    /// <summary>Regras que o texto de um script/migração permite conferir → conferência embutida.</summary>
    private static readonly Dictionary<string, string> Builtins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["G4"] = SqlStandardChecks.UnnamedConstraint, ["DF2"] = SqlStandardChecks.UnnamedConstraint,
        ["CK3"] = SqlStandardChecks.UnnamedConstraint, ["T7"] = SqlStandardChecks.UnnamedConstraint, ["PK10"] = SqlStandardChecks.UnnamedConstraint,
        ["G6"] = SqlStandardChecks.UnqualifiedObject, ["P6"] = SqlStandardChecks.UnqualifiedObject, ["V7"] = SqlStandardChecks.UnqualifiedObject,
        ["G7"] = SqlStandardChecks.EfCascade, ["DF5"] = SqlStandardChecks.EfUnnamedDefault,
        ["N4"] = SqlStandardChecks.ObjectUnderscore, ["V3"] = SqlStandardChecks.ObjectUnderscore,
        ["N14"] = SqlStandardChecks.ProcedurePrefix, ["P2"] = SqlStandardChecks.ProcedurePrefix, ["P5"] = SqlStandardChecks.ProcedurePrefix,
        ["V2"] = SqlStandardChecks.ViewPrefix,
        ["C1"] = SqlStandardChecks.SelectStar, ["V6"] = SqlStandardChecks.SelectStar,
        ["C2"] = SqlStandardChecks.MoneyType,
        ["S1"] = SqlStandardChecks.SchemaName, ["S2"] = SqlStandardChecks.SchemaName, ["S3"] = SqlStandardChecks.SchemaName,
        ["S5"] = SqlStandardChecks.NewObjectInDbo,
        ["T4"] = SqlStandardChecks.TablePrefix, ["T1"] = SqlStandardChecks.TableName,
        ["PK3"] = SqlStandardChecks.PkName, ["FK3"] = SqlStandardChecks.FkName, ["FK4"] = SqlStandardChecks.FkName,
        ["FK5"] = SqlStandardChecks.Cascade, ["FK6"] = SqlStandardChecks.Cascade, ["R5"] = SqlStandardChecks.Cascade,
        ["FK9"] = SqlStandardChecks.NoCheck, ["R6"] = SqlStandardChecks.NoCheck, ["CK5"] = SqlStandardChecks.NoCheck,
        ["IX3"] = SqlStandardChecks.IndexName, ["IX4"] = SqlStandardChecks.IndexName, ["IX5"] = SqlStandardChecks.IndexName,
        ["IXU1"] = SqlStandardChecks.IndexName,
        ["DF1"] = SqlStandardChecks.DefaultName, ["CK1"] = SqlStandardChecks.CheckName,
        ["P7"] = SqlStandardChecks.CreateOrAlterProcedure
    };

    public static string Version(IEnumerable<WikiPage> pages) =>
        pages.Select(p => VersionRow().Match(p.Markdown)).FirstOrDefault(m => m.Success)?.Groups["v"].Value ?? "?";

    public static List<ParsedStandard> Parse(IReadOnlyList<WikiPage> pages, string? platform = "sqlserver")
    {
        var version = Version(pages);
        var result = new List<ParsedStandard>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var page in pages)
        {
            var pattern = PagePattern().Match(page.Markdown) is { Success: true } pm ? pm.Groups["p"].Value : null;
            var validation = ValidationSql(page.Markdown);
            foreach (var line in page.Markdown.Split('\n'))
            {
                var m = RuleRow().Match(line.TrimEnd('\r'));
                if (!m.Success) continue;
                var code = m.Groups["id"].Value;
                if (!seen.Add(code)) continue;
                var cells = m.Groups["rest"].Value.Split('|').Select(c => c.Trim()).ToList();
                if (cells.Count < 2) continue;
                var en = Plain(cells[0]);
                var pt = Plain(cells[1]);
                var example = cells.Count > 2 ? cells[2] : null;
                var isNew = m.Groups["tag"].Value.Contains("NEW");
                var topic = Topics.First(t => code.StartsWith(t.Prefix, StringComparison.Ordinal)).Topic;
                var (good, bad) = Examples(example);
                if (pattern is not null && good.Count == 0 && code.EndsWith('1') && code.Length <= 4) good.Add(pattern);

                var check = Builtins.TryGetValue(code, out var builtin)
                    ? new StandardCheck { Kind = StandardCheck.Builtin, Pattern = builtin, Target = "sql" }
                    : validation is not null && topic is not ("general" or "naming" or "query")
                        ? new StandardCheck { Kind = StandardCheck.SqlKind, Sql = validation, Target = "database" }
                        : new StandardCheck();

                var data = new StandardData
                {
                    Code = code,
                    Area = StandardArea.Database,
                    Topic = topic,
                    Title = Title(pt),
                    TextPt = pt,
                    TextEn = en,
                    Examples = new StandardExamples { Good = good, Bad = bad },
                    AppliesTo = new StandardScope
                    {
                        Platforms = platform is null ? [] : [platform],
                        Objects = ["new", "migrated"]
                    },
                    Source = new StandardSource { Kind = "wiki", Ref = code, Version = version, Url = page.Url },
                    Status = isNew ? StandardStatus.Proposed : StandardStatus.Active,
                    Effect = GovernanceEffect.Monitor,
                    Check = check,
                    Order = ++order
                };
                var hash = Hash(pt + "\n" + en + "\n" + example);
                data = data with { Source = new StandardSource { Kind = "wiki", Ref = code, Version = version, Url = page.Url, Hash = hash } };
                result.Add(new ParsedStandard("db-" + code.ToLowerInvariant(), data, isNew, hash));
            }
        }
        return result;
    }

    /// <summary>Primeiro bloco SQL depois de um título "Validation" (a consulta que lista o que está fora do padrão).</summary>
    private static string? ValidationSql(string md)
    {
        var idx = md.IndexOf("Validation script", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return SqlBlock().Match(md, idx) is { Success: true } m ? m.Groups["sql"].Value.Trim() : null;
    }

    private static (List<string> Good, List<string> Bad) Examples(string? cell)
    {
        var good = new List<string>();
        var bad = new List<string>();
        if (string.IsNullOrWhiteSpace(cell)) return (good, bad);
        // "`CreateDate`, not `Create Date`" → certo antes do "not", errado depois.
        var parts = Regex.Split(cell, @",?\s*\bnot\b\s*", RegexOptions.IgnoreCase);
        good.AddRange(Codes(parts[0]));
        if (parts.Length > 1) bad.AddRange(parts.Skip(1).SelectMany(Codes));
        return (good, bad);
    }

    private static IEnumerable<string> Codes(string s) =>
        Regex.Matches(s, "`([^`]+)`").Select(m => m.Groups[1].Value.Trim()).Where(v => v.Length > 0);

    private static string Plain(string cell) => Link().Replace(cell, "${t}").Replace("**", string.Empty).Trim();

    private static string Title(string pt)
    {
        var first = Regex.Split(pt, @"(?<=[.:;])\s").First().Trim().TrimEnd('.', ':', ';');
        return first.Length <= 140 ? first : first[..137] + "…";
    }

    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
}
