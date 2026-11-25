using System.Text;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Extensions;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// 0075: as etapas da análise que publicam texto (padrão <c>montar-analise</c> e <c>propor-solucoes</c>, config
/// <see cref="SkillsConfigurationKeys.CodeRefGateSteps"/>) só concluem quando toda citação <c>caminho:linha</c> e todo
/// link <c>ref:id</c> do texto têm a fotografia do código no card — senão o usuário vê código sem o popover. Lê os
/// arquivos <c>.md</c> de análise da etapa e o checkpoint dela.
/// </summary>
public class CodeCitationGate(IExecutionPlanApplication plans, IPluginConfigurationResolver resolver, ILogger<CodeCitationGate> logger)
{
    public static readonly string[] DefaultSteps = ["montar-analise", "propor-solucoes"];
    private const int MaxListed = 8;

    public async Task<string?> CheckAsync(Guid planId, string stepKey, CancellationToken ct)
    {
        var key = ExecutionStep.NormalizeKey(stepKey);
        if (!(await GateStepsAsync(ct)).Contains(key))
            return null;

        var plan = await plans.GetAsync(planId, ct);
        var text = new StringBuilder(plan.Steps.FirstOrDefault(s => s.Key == key)?.Checkpoint ?? string.Empty);
        foreach (var artifact in plan.Artifacts.Where(a => a.NoteId is null && a.Kind == ExecutionArtifactKind.Analysis
                                                            && a.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                                                            && (a.StepKey == key || OwnedByStep(a.Name, key))))
        {
            var file = await plans.GetArtifactFileAsync(planId, artifact.Id, ct);
            text.Append('\n').Append(Encoding.UTF8.GetString(file.Data));
        }

        var heads = await plans.GetCardCodeRefHeadsAsync(plan.CardNumber, ct);
        return Missing(text.ToString(), heads, plan.CardNumber);
    }

    /// <summary>
    /// 0087: itens de trabalho e plano de correção criados por skill/especialista. Toda citação de código do texto precisa da
    /// fotografia (como na análise) e toda sub-tarefa/etapa de código (<c>kind = code</c>) precisa citar ao menos um trecho
    /// (<c>[caminho:linhas](ref:id)</c>) — o item diz exatamente o que muda e onde, com o popover do código.
    /// </summary>
    public async Task<string?> CheckWorkAsync(string card, IEnumerable<string?> texts, IEnumerable<(string? Title, string? Kind, string? Description)> parts,
        CancellationToken ct)
    {
        var text = string.Join('\n', texts.Where(t => !string.IsNullOrWhiteSpace(t)));
        var heads = await plans.GetCardCodeRefHeadsAsync(card, ct);
        if (Missing(text, heads, card, "crie de novo") is { } missing)
            return missing;
        return WithoutCodeCitation(parts, card);
    }

    /// <summary>Sub-tarefas/etapas de código sem nenhuma citação (null = tudo certo). Público para teste.</summary>
    public static string? WithoutCodeCitation(IEnumerable<(string? Title, string? Kind, string? Description)> parts, string card)
    {
        var bare = parts
            .Where(p => string.Equals(p.Kind?.Trim(), ExecutionStepKind.Code, StringComparison.OrdinalIgnoreCase)
                        && CodeCitations.RefLinks(p.Description).Count == 0 && CodeCitations.Extract(p.Description).Count == 0)
            .Select(p => $"\"{p.Title?.Trim()}\"")
            .ToList();
        if (bare.Count == 0)
            return null;
        return "0087: etapa/sub-tarefa de código precisa dizer exatamente o que muda e onde — cite o trecho com [caminho:linhas](ref:<ref>) " +
               $"(fotografia gravada antes com `prmake-plan.sh coderef {card} <ref> --repo <repositorio> --file <caminho> --lines A-B`; arquivo novo: `--change create --path <caminho>`), " +
               "explique o porquê em 1–2 frases e cite as regras da Base Solvace (`modulo#RN-012`). Sem citação: " + List(bare) + '.';
    }

    /// <summary>Texto das citações sem fotografia (null = tudo certo). Público para teste.</summary>
    public static string? Missing(string text, IReadOnlyList<ExecutionCodeRefHeadResponse> heads, string card, string retry = "conclua a etapa de novo")
    {
        var missing = CodeCitations.Extract(text)
            .Where(c => !heads.Any(h => ExecutionCodeRef.Matches(h.Path, h.Repository, h.StartLine, h.EndLine, c.Path, c.Start, c.End)))
            .Select(c => c.ToString())
            .ToList();
        var known = heads.Select(h => h.Ref).ToHashSet();
        var unknownRefs = CodeCitations.RefLinks(text).Where(r => !known.Contains(r)).ToList();
        if (missing.Count == 0 && unknownRefs.Count == 0)
            return null;

        var sb = new StringBuilder("0075: o texto cita código sem a fotografia — o usuário veria o código sem o popover.");
        if (missing.Count > 0)
            sb.Append(" Citações sem fotografia: ").Append(List(missing)).Append('.');
        if (unknownRefs.Count > 0)
            sb.Append(" Links ref: sem fotografia: ").Append(List(unknownRefs)).Append('.');
        sb.Append($" Grave cada trecho com `prmake-plan.sh coderef {card} <ref> --repo <repositorio> --file <caminho> --lines A-B` ")
          .Append($"(+ --change/--why/--after quando muda) e cite com [caminho:linha](ref:<ref>); depois {retry}.");
        return sb.ToString();
    }

    private static string List(IReadOnlyList<string> items) =>
        string.Join(", ", items.Take(MaxListed)) + (items.Count > MaxListed ? $" (e mais {items.Count - MaxListed})" : "");

    private static bool OwnedByStep(string name, string key) =>
        (key == "montar-analise" && name.StartsWith("analise", StringComparison.OrdinalIgnoreCase))
        || (key == "propor-solucoes" && name.StartsWith("solucoes", StringComparison.OrdinalIgnoreCase));

    private async Task<HashSet<string>> GateStepsAsync(CancellationToken ct)
    {
        try
        {
            var config = await resolver.GetEffectiveConfigurationAsync(SkillsConfigurationKeys.PluginName, ct);
            var values = (string.IsNullOrWhiteSpace(config.Options) ? null : config.Options.JsonToListOfDictionaries().FirstOrDefault())
                         ?? new Dictionary<string, string>();
            var raw = values.FirstOrDefault(kv => kv.Key.Equals(SkillsConfigurationKeys.CodeRefGateSteps, StringComparison.OrdinalIgnoreCase));
            if (raw.Key is not null)
                return raw.Value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.ToLowerInvariant()).ToHashSet();
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            logger.LogWarning("Plugin {Plugin} indisponível: {Error} — citações de código com as etapas padrão.", SkillsConfigurationKeys.PluginName, e.Message);
        }
        return DefaultSteps.ToHashSet();
    }
}
