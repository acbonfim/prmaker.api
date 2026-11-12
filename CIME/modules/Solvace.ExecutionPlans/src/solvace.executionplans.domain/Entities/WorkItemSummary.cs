using System.Text;

namespace solvace.executionplans.domain.Entities;

/// <summary>Resumo dos itens de trabalho do card para o contexto do especialista.</summary>
public static class WorkItemSummary
{
    /// <summary>Contexto compacto: quantos itens em andamento (com sub-tarefas), pendentes e fechados entram por extenso.</summary>
    public const int ActiveItems = 15;
    public const int PendingItems = 25;
    public const int ClosedItems = 10;

    /// <summary>
    /// Card com muitos itens (ex.: 110 itens de gaps no 78161) virava ~80 KB no contexto do especialista, relido em toda
    /// chamada da sessão. Aqui: itens em andamento com as sub-tarefas abertas, pendentes e fechados numa linha só, e o
    /// resto contado — o detalhe de um item é prmake_work_item(card, n); a lista inteira, prmake_work_items.
    /// </summary>
    public static void AppendCompact(StringBuilder sb, IReadOnlyList<WorkItem> items)
    {
        static string Head(WorkItem item) =>
            $"- **#{item.Number}** [{item.Type}] {item.Title} — **{item.Status}** " +
            $"({item.Tasks.Count(t => WorkTaskStatus.IsDone(t.Status))}/{item.Tasks.Count}) · fingerprint: `{item.Fingerprint}`";

        var active = items.Where(i => !WorkItemStatus.IsClosed(i.Status) && i.Status != WorkItemStatus.Pending)
            .OrderByDescending(i => i.UpdatedAt).ToList();
        var pending = items.Where(i => i.Status == WorkItemStatus.Pending).OrderByDescending(i => i.UpdatedAt).ToList();
        var closed = items.Where(i => WorkItemStatus.IsClosed(i.Status)).OrderByDescending(i => i.UpdatedAt).ToList();
        sb.AppendLine($"{active.Count} em andamento · {pending.Count} pendentes · {closed.Count} fechados. " +
                      "Não crie item igual a um destes (mesmo fingerprint) — o PRMake recusa duplicata; lista completa em " +
                      "prmake_work_items e o detalhe de um item em prmake_work_item(card, n).");

        if (active.Count > 0) sb.AppendLine().AppendLine("Em andamento (mais recentes primeiro):");
        foreach (var item in active.Take(ActiveItems))
        {
            sb.AppendLine(Head(item));
            foreach (var t in item.OrderedTasks.Where(t => WorkTaskStatus.IsOpen(t.Status) || t.Status == WorkTaskStatus.Failed))
            {
                var keyParams = t.Params.Where(p => WorkItem.FingerprintParams.Contains(p.Key.ToLowerInvariant()) && p.Value.Length > 0)
                    .Select(p => $"{p.Key}={p.Value}").ToList();
                sb.AppendLine($"  - #{t.Number} `{t.Kind}` {t.Title} — {t.Status}" +
                              (keyParams.Count > 0 ? $" · {string.Join(", ", keyParams)}" : string.Empty));
            }
            foreach (var link in item.Links.Where(l => l.Kind is WorkItemLinkKind.PullRequest))
                sb.AppendLine($"  - {link.Kind}: {link.Title ?? link.Url} ({link.Url})");
        }
        if (pending.Count > 0) sb.AppendLine().AppendLine("Pendentes (não começaram):");
        foreach (var item in pending.Take(PendingItems)) sb.AppendLine(Head(item));
        if (closed.Count > 0) sb.AppendLine().AppendLine("Fechados mais recentes:");
        foreach (var item in closed.Take(ClosedItems)) sb.AppendLine(Head(item));

        var hidden = Math.Max(0, active.Count - ActiveItems) + Math.Max(0, pending.Count - PendingItems) +
                     Math.Max(0, closed.Count - ClosedItems);
        if (hidden > 0)
            sb.AppendLine().AppendLine($"+ {hidden} itens fora deste resumo (prmake_work_items lista todos; prmake_work_item(card, n) traz um).");
    }
}
