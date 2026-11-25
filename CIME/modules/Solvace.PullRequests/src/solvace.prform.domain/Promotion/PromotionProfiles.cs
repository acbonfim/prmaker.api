using System.Text.Json;
using System.Text.RegularExpressions;

namespace solvace.prform.domain.Promotion;

/// <summary>Como a promoção chega ao destino: cherry-pick dos commits da branch principal ou PR direto dela.</summary>
public static class PromotionMode
{
    public const string CherryPick = "cherry-pick";
    public const string Direct = "direct";

    public static string Normalize(string? mode) =>
        string.Equals(mode?.Trim(), Direct, StringComparison.OrdinalIgnoreCase) ? Direct : CherryPick;
}

/// <summary>Um destino de um perfil: o modelo do nome (<c>release-{version}</c>, <c>master</c>…), se abre como rascunho e o modo.</summary>
public record PromotionTarget(string Branch, bool Draft = false, string Mode = PromotionMode.CherryPick);

public record PromotionProfile(string Key, string Label, IReadOnlyList<PromotionTarget> Targets);

/// <summary>
/// 0074: perfis de destino por tipo de repositório (chave <c>PromotionProfiles</c> do "Skills Configurations"):
/// <c>{"profiles": {"legacy": {"label": "Netcore", "targets": [{"branch": "hotfix-{version}"}]}}}</c>.
/// A chave de cada perfil é o <c>kind</c> do <c>BranchStrategy.repositories</c>. Nada de nome de branch fica no código.
/// </summary>
public class PromotionProfileSet
{
    private readonly Dictionary<string, PromotionProfile> _profiles;

    private PromotionProfileSet(Dictionary<string, PromotionProfile> profiles) => _profiles = profiles;

    public IReadOnlyCollection<PromotionProfile> All => _profiles.Values;

    public PromotionProfile? Find(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) && _profiles.TryGetValue(kind.Trim(), out var p) ? p : null;

    public static PromotionProfileSet Empty { get; } = new([]);

    /// <summary>JSON ausente/inválido = conjunto vazio (a tela avisa que não há perfil configurado).</summary>
    public static PromotionProfileSet Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGet(doc.RootElement, "profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
                return Empty;

            var map = new Dictionary<string, PromotionProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in profiles.EnumerateObject())
            {
                var targets = new List<PromotionTarget>();
                if (TryGet(entry.Value, "targets", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in list.EnumerateArray())
                    {
                        var branch = TryGet(t, "branch", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()?.Trim() : null;
                        if (string.IsNullOrWhiteSpace(branch))
                            continue;
                        var draft = TryGet(t, "draft", out var d) && d.ValueKind == JsonValueKind.True;
                        var mode = TryGet(t, "mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                        targets.Add(new PromotionTarget(branch, draft, PromotionMode.Normalize(mode)));
                    }
                }
                var label = TryGet(entry.Value, "label", out var l) && l.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(l.GetString())
                    ? l.GetString()!.Trim()
                    : entry.Name;
                map[entry.Name] = new PromotionProfile(entry.Name, label, targets);
            }
            return new PromotionProfileSet(map);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var p in element.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        return false;
    }
}

/// <summary>Tipo do repositório pelo <c>BranchStrategy.repositories</c>: a primeira regra que casa (glob, sem diferenciar maiúsculas) — a mesma regra da skill.</summary>
public static class BranchStrategyKinds
{
    public static string? KindOf(string? repository, string? branchStrategyJson)
    {
        var name = repository?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(branchStrategyJson))
            return null;
        // "owner/repo" → só o nome do repositório, como nas regras.
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];

        try
        {
            using var doc = JsonDocument.Parse(branchStrategyJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("repositories", out var repos) || repos.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var rule in repos.EnumerateArray())
            {
                var match = rule.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                var kind = rule.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
                if (string.IsNullOrWhiteSpace(match) || string.IsNullOrWhiteSpace(kind))
                    continue;
                if (GlobMatches(match.Trim(), name))
                    return kind.Trim();
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private static bool GlobMatches(string pattern, string value)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
