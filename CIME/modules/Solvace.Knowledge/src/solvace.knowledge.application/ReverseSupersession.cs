using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Reverse;

namespace solvace.knowledge.application;

/// <summary>
/// Uma fonte por módulo (0054): seção antiga da Base Solvace cujos documentos da engenharia reversa (todos) estão
/// publicados fica "substituída" — sai do espelho, do índice, da busca, do Pergunte e do MCP; continua na tela como
/// histórico. Publicação parcial não substitui nada. <c>@armadilhas</c> = armadilhas antigas já migradas para os itens.
/// </summary>
public static class ReverseSupersession
{
    public const string TrapsMarker = "@armadilhas";

    /// <summary>Seções substituídas do projeto: chave → "substituída por …".</summary>
    public static Dictionary<string, string> Compute(ArchitectureProject project, ReverseSettings settings, ReverseModule? module)
    {
        var map = settings.Supersedes ?? ReverseSettings.DefaultSupersedes;
        var published = ReverseDocTypes.All.Where(t => project.Sections.Any(x => x.Key == t.SectionKey)).Select(t => t.Key).ToHashSet();
        var result = new Dictionary<string, string>();
        if (published.Count == 0 && module?.TrapsMigratedAt is null) return result;
        foreach (var section in project.Sections)
        {
            if (section.Key.StartsWith(ReverseDocTypes.SectionPrefix, StringComparison.Ordinal)) continue;
            var docs = map.TryGetValue(section.Key, out var exact) ? exact
                : section.Key.StartsWith(ArchitectureSectionAudience.GuidePrefix, StringComparison.Ordinal) && map.TryGetValue("guia-*", out var guide) ? guide
                : null;
            if (docs is not { Count: > 0 }) continue;
            var covered = docs.All(d => d == TrapsMarker ? module?.TrapsMigratedAt is not null : published.Contains(d));
            if (covered)
                result[section.Key] = "substituída por " + string.Join(" + ", docs.Select(d => d == TrapsMarker
                    ? "armadilhas da engenharia reversa"
                    : ReverseDocTypes.ByKey.TryGetValue(d, out var t) ? t.Title : d));
        }
        return result;
    }

    /// <summary>Tira as seções substituídas dos projetos carregados sem rastreamento (espelho, busca, catálogo).</summary>
    public static void Strip(IEnumerable<ArchitectureProject> projects, ReverseSettings settings, IReadOnlyCollection<ReverseModule> modules)
    {
        var byKey = modules.ToDictionary(m => m.Key);
        foreach (var p in projects)
        {
            var superseded = Compute(p, settings, byKey.GetValueOrDefault(p.Key));
            if (superseded.Count > 0) p.Sections.RemoveAll(s => superseded.ContainsKey(s.Key));
        }
    }

    /// <summary>Armadilhas do módulo em markdown (a seção técnica "Armadilhas" do espelho, gerada delas).</summary>
    public static string RenderTraps(ArchitectureProject project, IEnumerable<ReverseTrap> traps)
    {
        var sb = new System.Text.StringBuilder($"<!-- {project.Key}/armadilhas · gerada das armadilhas da engenharia reversa -->\n# {project.Name} — Armadilhas\n\n");
        foreach (var t in traps.OrderBy(t => t.Title))
        {
            sb.AppendLine($"## {t.Title}{(t.NeedsReview ? " (a conferir)" : "")}");
            if (t.Items.Count > 0) sb.AppendLine($"- **Itens:** {string.Join(", ", t.Items)}");
            if (t.Cards.Count > 0) sb.AppendLine($"- **Cards:** {string.Join(", ", t.Cards)}");
            sb.AppendLine().AppendLine(t.Text).AppendLine();
        }
        return sb.ToString();
    }
}
