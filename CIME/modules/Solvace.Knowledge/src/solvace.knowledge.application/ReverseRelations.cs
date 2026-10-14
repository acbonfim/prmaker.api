using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Reverse;

namespace solvace.knowledge.application;

/// <summary>
/// Relações efetivas dos projetos (0066): as do extrator/curadoria (gravadas no projeto) + as integrações (<c>INT</c>) da
/// engenharia reversa publicada, resolvidas na leitura a partir do índice (<see cref="ReverseIntegrations"/>). Assim os
/// documentos já publicados ficam corretos sem republicar, e as relações <c>re#</c> que a publicação gravava antes (com
/// destino em texto livre) deixam de ser lidas — os dados continuam no banco.
/// </summary>
public static class ReverseRelations
{
    /// <summary>Evidência das relações que vêm da engenharia reversa (<c>re#INT-001</c> — mesmo formato de antes da 0066).</summary>
    public const string EvidencePrefix = "re#";

    private static readonly object Gate = new();
    private static string _stamp = string.Empty;
    private static List<ReverseIntegration> _integrations = [];

    public static bool FromReverse(ArchitectureRelation relation) =>
        relation.Evidence?.StartsWith(EvidencePrefix, StringComparison.Ordinal) == true;

    /// <summary>Destinos possíveis: cada projeto com nome, nome amigável e apelidos (campo Module do card).</summary>
    public static List<IntegrationTarget> Targets(IEnumerable<ArchitectureProject> projects, IEnumerable<ReverseModule> modules)
    {
        var aliases = modules.ToDictionary(m => m.Key, m => m.Aliases, StringComparer.OrdinalIgnoreCase);
        return projects.Select(p => new IntegrationTarget(p.Key,
            new[] { p.Name, p.DisplayName }.Concat(aliases.GetValueOrDefault(p.Key) ?? [])
                .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct().ToList())).ToList();
    }

    /// <summary>Integrações publicadas (todos os documentos), resolvidas — em cache até o índice ou os projetos mudarem.</summary>
    public static async Task<List<ReverseIntegration>> IntegrationsAsync(IKnowledgeRepository repository, IReadOnlyCollection<ArchitectureProject> projects,
        IReadOnlyCollection<ReverseModule>? modules, CancellationToken cancellationToken)
    {
        modules ??= await repository.GetReverseModulesAsync(cancellationToken);
        var index = await repository.GetIndexStampAsync(cancellationToken);
        var stamp = $"{index.Count}:{index.LastUpdate?.UtcTicks}|"
                    + string.Join(",", projects.Select(p => $"{p.Key}:{p.Name}:{p.DisplayName}").OrderBy(x => x, StringComparer.Ordinal)) + "|"
                    + string.Join(",", modules.Select(m => $"{m.Key}:{string.Join("/", m.Aliases)}").OrderBy(x => x, StringComparer.Ordinal));
        lock (Gate)
            if (stamp == _stamp) return _integrations;
        // só os INT: antes lia o índice inteiro (texto de todos os itens + cópia normalizada da busca) para usar uma fração,
        // e numa instância nova isso estourava os 512 MiB do Cloud Run
        var entries = (await repository.GetIndexEntriesByKindAsync("INT", cancellationToken)).Where(e => !e.Removed).ToList();
        var targets = Targets(projects, modules);
        // o ID é único no módulo; se aparecer em dois documentos, vale o mais novo
        var result = entries.GroupBy(e => (e.ModuleKey, e.ItemId)).Select(g => g.OrderByDescending(e => e.UpdatedAt).First())
            .OrderBy(e => e.ModuleKey, StringComparer.Ordinal).ThenBy(e => e.ItemId, StringComparer.Ordinal)
            .Select(e => ReverseIntegrations.Read(e.ModuleKey, e.ItemId, e.Title, e.Body, targets)).ToList();
        lock (Gate)
        {
            _stamp = stamp;
            _integrations = result;
        }
        return result;
    }

    /// <summary>Troca, em memória (projetos lidos sem rastreamento), as relações <c>re#</c> gravadas pelas integrações resolvidas.</summary>
    public static void Apply(IEnumerable<ArchitectureProject> projects, IReadOnlyCollection<ReverseIntegration> integrations)
    {
        var bySource = integrations.ToLookup(i => i.Source, StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
        {
            var kept = p.Relations.Where(r => !FromReverse(r)).ToList();
            var fromReverse = bySource[p.Key]
                .SelectMany(i => i.Targets.Select(t => new ArchitectureRelation { Target = t, Kind = i.Kind, Detail = Detail(i), Evidence = EvidencePrefix + i.ItemId }));
            // a mesma ligação nas duas origens fica uma vez de cada (o mapa junta pela origem/destino/tipo)
            p.SetRelations(kept.Concat(fromReverse).Take(ArchitectureProject.MaxRelations).ToList());
        }
    }

    /// <summary>Lê as integrações e aplica nos projetos. Devolve as integrações (o mapa usa para listar os itens de cada ligação).</summary>
    public static async Task<List<ReverseIntegration>> ApplyAsync(IKnowledgeRepository repository, List<ArchitectureProject> projects,
        IReadOnlyCollection<ReverseModule>? modules, CancellationToken cancellationToken)
    {
        var integrations = await IntegrationsAsync(repository, projects, modules, cancellationToken);
        Apply(projects, integrations);
        return integrations;
    }

    /// <summary>Detalhe da relação: o título do item e o mecanismo ("… · fila SQS NOTIFICATION_WORKER").</summary>
    public static string Detail(ReverseIntegration i)
    {
        var detail = i.Title;
        if (!string.IsNullOrWhiteSpace(i.Mechanism)) detail += " · " + i.Mechanism;
        if (i.ToConfirm) detail += " (a confirmar)";
        return detail.Length <= 400 ? detail : detail[..400];
    }

    /// <summary>Esquece o cache (testes).</summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _stamp = string.Empty;
            _integrations = [];
        }
    }
}
