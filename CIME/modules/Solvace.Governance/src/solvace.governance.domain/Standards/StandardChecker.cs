using System.Text.RegularExpressions;
using solvace.governance.domain.Engine;
using solvace.governance.domain.Entities;

namespace solvace.governance.domain.Standards;

/// <summary>O que conferir: texto gravado (sql | migration | code) e/ou caminho do arquivo, com o contexto do card.</summary>
public sealed record StandardCheckRequest
{
    public string? Text { get; init; }
    /// <summary>sql | migration | code | path.</summary>
    public string Target { get; init; } = "sql";
    public string? Path { get; init; }
    public string? Repository { get; init; }
    public IReadOnlyCollection<string>? Modules { get; init; }
}

public sealed record StandardFinding(string Key, string Code, string Title, string Effect, int? Line, string Excerpt, string Hint);

public sealed record StandardCheckResult(string Decision, string Mode, IReadOnlyList<StandardFinding> Findings, string WouldDecide)
{
    public string Outcome => Decision switch
    {
        GovernanceDecisionKind.Block => GovernanceOutcome.Blocked,
        GovernanceDecisionKind.Warn => GovernanceOutcome.Warned,
        _ => Findings.Count > 0 ? GovernanceOutcome.Monitored : GovernanceOutcome.Ok
    };
}

/// <summary>
/// 0101: confere um texto/caminho contra os padrões que valem para ele. Padrão proposto/descontinuado só monitora;
/// no modo monitor da governança nada barra (<see cref="StandardCheckResult.WouldDecide"/> diz o que o enforce faria).
/// Conferência <c>sql</c> (consulta de validação da wiki) não roda aqui: é sob demanda, no banco de um ambiente.
/// </summary>
public static class StandardChecker
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public static StandardCheckResult Check(StandardCheckRequest request, IEnumerable<GovernanceStandard> standards, GovernanceSettings settings)
    {
        if (settings.Mode == GovernanceMode.Off)
            return new StandardCheckResult(GovernanceDecisionKind.Allow, settings.Mode, [], GovernanceDecisionKind.Allow);

        var target = (request.Target ?? "sql").ToLowerInvariant();
        var applicable = standards
            .Where(s => s.Status != StandardStatus.Deprecated && s.Check.Kind != StandardCheck.None && s.Check.Kind != StandardCheck.SqlKind)
            .Where(s => s.AppliesTo.AppliesTo(request.Repository, request.Modules))
            .ToList();

        var findings = new List<StandardFinding>();
        var text = request.Text ?? string.Empty;

        // Embutidas: uma passada só no texto, com todas as conferências pedidas pelos padrões.
        var builtin = applicable.Where(s => s.Check.Kind == StandardCheck.Builtin && TargetFits(s.Check.Target, target) && text.Length > 0).ToList();
        if (builtin.Count > 0)
        {
            var hits = SqlStandardChecks.Run(text, builtin.Select(s => s.Check.Pattern!).Distinct().ToList(), target);
            foreach (var hit in hits)
                foreach (var s in builtin.Where(s => s.Check.Pattern == hit.Check).OrderBy(s => s.Order).Take(1))
                    findings.Add(new StandardFinding(s.Key, s.Code, s.Title, s.EffectiveEffect, hit.Line, hit.Excerpt, hit.Hint));
        }

        foreach (var s in applicable.Where(s => s.Check.Kind == StandardCheck.Regex && TargetFits(s.Check.Target, target) && text.Length > 0))
            findings.AddRange(RegexFindings(s, text));

        if (request.Path is { Length: > 0 } path)
            // Caminho: Target = de quais arquivos o padrão trata (glob; vazio = todos); Pattern = onde devem (MustMatch) ou
            // não podem ficar.
            foreach (var s in applicable.Where(s => s.Check.Kind == StandardCheck.Path
                                                    && (s.Check.Target is null || s.Check.Target.Split('|').Any(g => GovernanceRuleEngine.PathGlob(g.Trim(), path)))))
            {
                var matches = s.Check.Pattern!.Split('|').Any(g => GovernanceRuleEngine.PathGlob(g.Trim(), path));
                if (matches != s.Check.MustMatch)
                    findings.Add(new StandardFinding(s.Key, s.Code, s.Title, s.EffectiveEffect, null, path,
                        s.Check.Hint ?? (s.Check.MustMatch ? $"Arquivo fora da estrutura esperada ({s.Check.Pattern})." : $"Caminho não permitido ({s.Check.Pattern}).")));
            }

        var strongest = findings.Select(f => GovernanceEffect.Weight(f.Effect)).DefaultIfEmpty(0).Max();
        var would = strongest switch { 3 => GovernanceDecisionKind.Block, 2 => GovernanceDecisionKind.Warn, _ => GovernanceDecisionKind.Allow };
        var decision = settings.Mode == GovernanceMode.Enforce ? would : GovernanceDecisionKind.Allow;
        return new StandardCheckResult(decision, settings.Mode, findings, would);
    }

    private static IEnumerable<StandardFinding> RegexFindings(GovernanceStandard s, string text)
    {
        Regex rx;
        try { rx = new Regex(s.Check.Pattern!, RegexOptions.IgnoreCase | RegexOptions.Multiline, RegexTimeout); }
        catch (ArgumentException) { yield break; }
        MatchCollection matches;
        try { matches = rx.Matches(text); _ = matches.Count; }
        catch (RegexMatchTimeoutException) { yield break; }

        if (s.Check.MustMatch)
        {
            if (matches.Count == 0)
                yield return new StandardFinding(s.Key, s.Code, s.Title, s.EffectiveEffect, null, string.Empty, s.Check.Hint ?? s.Title);
            yield break;
        }
        foreach (Match m in matches.Take(20))
        {
            var line = text[..m.Index].Count(c => c == '\n') + 1;
            var excerpt = text.Split('\n')[line - 1].Trim();
            yield return new StandardFinding(s.Key, s.Code, s.Title, s.EffectiveEffect, line,
                excerpt.Length <= 160 ? excerpt : excerpt[..157] + "…", s.Check.Hint ?? s.Title);
        }
    }

    /// <summary>Padrão de banco (alvo sql) também vale para migração e código com SQL; alvo vazio = qualquer texto.</summary>
    private static bool TargetFits(string? standardTarget, string target) =>
        standardTarget is null || standardTarget == target || (standardTarget == "sql" && target is "migration" or "code");
}
