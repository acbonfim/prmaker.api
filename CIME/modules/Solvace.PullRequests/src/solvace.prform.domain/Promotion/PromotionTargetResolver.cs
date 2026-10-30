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
/// <param name="Expected">O nome esperado no remoto (com a versão trocada, quando o modelo tem {version}) — é o que a tela mostra quando não existe.</param>
public record ResolvedTarget(string Template, string? Branch, IReadOnlyList<string> Candidates, string Status, bool Draft, string Mode, string Expected);

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

    private static readonly Regex TrailingVersionWord = new(@"(?<=^|[-/_])version$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Acha no remoto a branch de um destino. Os nomes do PRMake são literais (<c>release-version</c>, <c>hotfix-version</c>,
    /// <c>edge</c>, <c>master</c> são branches de verdade): o nome escrito vale primeiro. <c>{version}</c> só entra quando o
    /// modelo o traz, ou quando o nome termina em "version" e não existe — aí tenta com o número (release-3.274.10).
    /// </summary>
    public static ResolvedTarget Resolve(PromotionTarget target, string? version, IReadOnlyCollection<string> remoteBranches)
    {
        var template = target.Branch.Trim();
        var mode = PromotionMode.Normalize(target.Mode);
        var v = NormalizeVersion(version);

        ResolvedTarget Result(string? branch, IReadOnlyList<string> candidates, string status, string expected) =>
            new(template, branch, candidates, status, target.Draft, mode, expected);

        if (!template.Contains(VersionToken, StringComparison.OrdinalIgnoreCase))
        {
            var exact = remoteBranches.FirstOrDefault(b => string.Equals(b, template, StringComparison.Ordinal))
                        ?? remoteBranches.FirstOrDefault(b => string.Equals(b, template, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return Result(exact, [exact], PromotionTargetStatus.Resolved, template);

            // "release-version" não existe como nome literal: pode ser a convenção com o número (release-3.274.10).
            if (v is not null && TrailingVersionWord.IsMatch(template))
            {
                var viaVersion = ResolveTemplated(TrailingVersionWord.Replace(template, VersionToken), v, remoteBranches);
                if (viaVersion.Status != PromotionTargetStatus.Missing)
                    return Result(viaVersion.Branch, viaVersion.Candidates, viaVersion.Status, viaVersion.Expected);
            }
            return Result(null, [], PromotionTargetStatus.Missing, template);
        }

        if (v is null)
            return Result(null, [], PromotionTargetStatus.Missing, template);
        var r = ResolveTemplated(template, v, remoteBranches);
        return Result(r.Branch, r.Candidates, r.Status, r.Expected);
    }

    private static (string? Branch, IReadOnlyList<string> Candidates, string Status, string Expected) ResolveTemplated(string template, string version, IReadOnlyCollection<string> remoteBranches)
    {
        var literal = Regex.Replace(template, Regex.Escape(VersionToken), version, RegexOptions.IgnoreCase);

        // 1) a substituição literal existe: é ela.
        var literalHit = remoteBranches.FirstOrDefault(b => string.Equals(b, literal, StringComparison.Ordinal));
        if (literalHit is not null)
            return (literalHit, [literalHit], PromotionTargetStatus.Resolved, literal);

        // 2) variações de separador e "V": release/3.274.10, release_3.274.10, release-V3.274.10…
        var pattern = BuildPattern(template, version);
        var candidates = remoteBranches.Where(b => pattern.IsMatch(b)).OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
        return candidates.Count switch
        {
            0 => (null, [], PromotionTargetStatus.Missing, literal),
            1 => (candidates[0], candidates, PromotionTargetStatus.Resolved, literal),
            _ => (null, candidates, PromotionTargetStatus.Ambiguous, literal)
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
