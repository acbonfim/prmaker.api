using System.Text.RegularExpressions;

namespace solvace.prform.domain.Promotion;

public static class PromotionTargetStatus
{
    public const string Resolved = "resolved";
    /// <summary>Nenhuma branch remota casa com o modelo (a release ainda não foi cortada?).</summary>
    public const string Missing = "missing";
    /// <summary>Mais de uma branch casa — o usuário escolhe.</summary>
    public const string Ambiguous = "ambiguous";
}

/// <summary>Resultado de resolver um modelo de destino (<c>release-{version}</c>) contra as branches do remoto.</summary>
public record ResolvedTarget(string Template, string? Branch, IReadOnlyList<string> Candidates, string Status, bool Draft, string Mode);

/// <summary>
/// 0074: troca <c>{version}</c> pelo número da versão e confere no remoto qual é o nome real da branch. A mensagem do
/// Release Manager diz "release-version"; o nome pode ser <c>release-3.274.10</c>, <c>release/3.274.10</c> ou <c>release-V3.274.10</c>.
/// </summary>
public static class PromotionTargetResolver
{
    public const string VersionToken = "{version}";
    private static readonly Regex VersionShape = new(@"^\d+(\.\d+){1,3}([-.][0-9A-Za-z]+)*$", RegexOptions.Compiled);

    /// <summary>"V3.274.10" / "v3.274.10" → "3.274.10"; formato inesperado → null.</summary>
    public static string? NormalizeVersion(string? raw)
    {
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v))
            return null;
        if (v.Length > 1 && (v[0] == 'v' || v[0] == 'V') && char.IsDigit(v[1]))
            v = v[1..];
        return VersionShape.IsMatch(v) ? v : null;
    }

    public static ResolvedTarget Resolve(PromotionTarget target, string? version, IReadOnlyCollection<string> remoteBranches)
    {
        var template = target.Branch.Trim();
        var mode = PromotionMode.Normalize(target.Mode);

        // Sem {version}: o nome é literal (master, edge) — confere só a existência.
        if (!template.Contains(VersionToken, StringComparison.OrdinalIgnoreCase))
        {
            var exact = remoteBranches.FirstOrDefault(b => string.Equals(b, template, StringComparison.Ordinal))
                        ?? remoteBranches.FirstOrDefault(b => string.Equals(b, template, StringComparison.OrdinalIgnoreCase));
            return exact is null
                ? new ResolvedTarget(template, null, [], PromotionTargetStatus.Missing, target.Draft, mode)
                : new ResolvedTarget(template, exact, [exact], PromotionTargetStatus.Resolved, target.Draft, mode);
        }

        var v = NormalizeVersion(version);
        if (v is null)
            return new ResolvedTarget(template, null, [], PromotionTargetStatus.Missing, target.Draft, mode);

        // 1) a substituição literal existe: é ela.
        var literal = Regex.Replace(template, Regex.Escape(VersionToken), v, RegexOptions.IgnoreCase);
        var literalHit = remoteBranches.FirstOrDefault(b => string.Equals(b, literal, StringComparison.Ordinal));
        if (literalHit is not null)
            return new ResolvedTarget(template, literalHit, [literalHit], PromotionTargetStatus.Resolved, target.Draft, mode);

        // 2) variações de separador e "V": release/3.274.10, release_3.274.10, release-V3.274.10…
        var pattern = BuildPattern(template, v);
        var candidates = remoteBranches.Where(b => pattern.IsMatch(b)).OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
        return candidates.Count switch
        {
            0 => new ResolvedTarget(template, null, [], PromotionTargetStatus.Missing, target.Draft, mode),
            1 => new ResolvedTarget(template, candidates[0], candidates, PromotionTargetStatus.Resolved, target.Draft, mode),
            _ => new ResolvedTarget(template, null, candidates, PromotionTargetStatus.Ambiguous, target.Draft, mode)
        };
    }

    private static Regex BuildPattern(string template, string version)
    {
        var parts = Regex.Split(template, Regex.Escape(VersionToken), RegexOptions.IgnoreCase);
        var sb = new System.Text.StringBuilder("^");
        for (var i = 0; i < parts.Length; i++)
        {
            foreach (var ch in parts[i])
                sb.Append(ch is '-' or '/' or '_' ? "[-/_]" : Regex.Escape(ch.ToString()));
            if (i < parts.Length - 1)
                sb.Append("[vV]?").Append(Regex.Escape(version));
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
