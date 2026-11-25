using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>
/// 0085 r5 (1.2): arquivo na seção "Arquivos" do item — do próprio item (<c>source:"item"</c>) ou do plano ligado
/// (<c>source:"plan"</c>, o artefato do plano). Projeção leve, sem conteúdo: o conteúdo sai de <see cref="ContentUrl"/>
/// (relativo a <c>api/v1/</c>).
/// </summary>
public class WorkItemFileResponse
{
    public const string SourceItem = "item";
    public const string SourcePlan = "plan";
    /// <summary>Arquivo da conversa do Especialista do caso (0080) — só na lista do card, sem item (<c>workItemNumber = 0</c>).</summary>
    public const string SourceCase = "case";
    public const string OriginEvidence = "evidence";
    public const string OriginComment = "comment";

    public Guid Id { get; set; }
    /// <summary><c>item</c> = arquivo do item; <c>plan</c> = arquivo do plano ligado; <c>case</c> = da conversa do especialista.</summary>
    public string Source { get; set; } = SourceItem;
    /// <summary>
    /// Só nos arquivos do plano: <c>evidence</c> = evidência do card no DevOps (0076), <c>comment</c> = anexo de comentário
    /// do plano; null = arquivo gravado pela skill/tela.
    /// </summary>
    public string? Origin { get; set; }
    /// <summary>Número do item de trabalho dono da lista.</summary>
    public long WorkItemNumber { get; set; }
    /// <summary>Item: número por item (<c>arquivo #n</c>). Plano: número por card do artefato (<c>anexo #n</c>).</summary>
    public int Number { get; set; }
    /// <summary>Sub-tarefa do arquivo (no plano: a sub-tarefa da mesma etapa); null = do item todo.</summary>
    public long? TaskNumber { get; set; }
    /// <summary>Só nos arquivos do plano: plano e etapa de origem.</summary>
    public Guid? PlanId { get; set; }
    public string? StepKey { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Description { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <summary>
    /// Rota relativa do conteúdo (<c>WorkItems/{n}/files/{id}/content</c>, <c>ExecutionPlan/{plano}/artifacts/{id}/content</c> ou
    /// <c>CaseChat/{card}/files/{id}/content</c>).
    /// </summary>
    public string ContentUrl { get; set; } = string.Empty;

    public static string ItemContentUrl(long workItemNumber, Guid fileId) => $"WorkItems/{workItemNumber}/files/{fileId}/content";
    public static string PlanContentUrl(Guid planId, Guid artifactId) => $"ExecutionPlan/{planId}/artifacts/{artifactId}/content";
    public static string CaseContentUrl(string cardNumber, Guid fileId) => $"CaseChat/{Uri.EscapeDataString(cardNumber)}/files/{fileId}/content";

    /// <summary>Arquivo da conversa do especialista: tipo pelo nome (script, dados...), imagem e o resto como anexo.</summary>
    public static WorkItemFileResponse FromCase(CaseFile f) => new()
    {
        Id = f.Id,
        Source = SourceCase,
        WorkItemNumber = 0,
        Number = f.Number,
        Name = f.Name,
        Kind = f.Kind == "file" ? WorkItemFileKind.Normalize(null, f.Name) : WorkItemFileKind.Attachment,
        ContentType = f.ContentType,
        Size = f.Size,
        CreatedBy = f.CreatedBy,
        CreatedAt = f.CreatedAt,
        ContentUrl = CaseContentUrl(f.CardNumber, f.Id),
    };

    public static WorkItemFileResponse From(WorkItemFile f, long workItemNumber) => new()
    {
        Id = f.Id,
        Source = SourceItem,
        WorkItemNumber = workItemNumber,
        Number = f.Number,
        TaskNumber = f.TaskNumber,
        Name = f.Name,
        Kind = f.Kind,
        ContentType = f.ContentType,
        Size = f.Size,
        CreatedBy = f.CreatedBy,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt,
        ContentUrl = ItemContentUrl(workItemNumber, f.Id),
    };

    public static WorkItemFileResponse FromPlan(ExecutionArtifact a, long workItemNumber, long? taskNumber) => new()
    {
        Id = a.Id,
        Source = SourcePlan,
        Origin = a.ExternalKey is not null ? OriginEvidence : a.NoteId is not null ? OriginComment : null,
        WorkItemNumber = workItemNumber,
        Number = a.Number,
        TaskNumber = taskNumber,
        PlanId = a.PlanId,
        StepKey = a.StepKey,
        Name = a.Name,
        Kind = a.Kind,
        ContentType = a.ContentType,
        Size = a.Size,
        Description = a.Description,
        CreatedBy = a.CreatedBy,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
        ContentUrl = PlanContentUrl(a.PlanId, a.Id),
    };
}

/// <summary>
/// 0085 r5: monta a lista "Arquivos" do item (os dele primeiro, depois os do plano ligado). Entram todos os arquivos do
/// plano — os da skill/tela, as evidências do DevOps e os anexos de comentário (<see cref="WorkItemFileResponse.Origin"/>):
/// a aba Arquivos mostra tudo o que o card tem, como a aba Anexos da Execução.
/// </summary>
public static class WorkItemFileListing
{
    /// <param name="taskByStep">Etapa do plano → número da sub-tarefa do item espelho (mesmo <c>StepKey</c>).</param>
    public static List<WorkItemFileResponse> Build(long workItemNumber, IEnumerable<WorkItemFile> own,
        IEnumerable<ExecutionArtifact> planArtifacts, IReadOnlyDictionary<string, long> taskByStep)
    {
        var list = own.OrderBy(f => f.Number).Select(f => WorkItemFileResponse.From(f, workItemNumber)).ToList();
        list.AddRange(planArtifacts
            .OrderBy(a => a.Number).ThenBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => WorkItemFileResponse.FromPlan(a, workItemNumber,
                a.StepKey is { } key && taskByStep.TryGetValue(key, out var task) ? task : null)));
        return list;
    }

    /// <summary>Etapa do plano → sub-tarefa do item (mesmo StepKey).</summary>
    public static IReadOnlyDictionary<string, long> TaskByStep(WorkItem item) =>
        item.Tasks.Where(t => t.StepKey is not null)
            .GroupBy(t => t.StepKey!)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Order).First().Number);

    /// <summary>
    /// 0087: junta os arquivos do card. Cada arquivo de plano entra no primeiro item (menor número) ligado ao plano — nunca
    /// em dois; o de plano sem item fica com <c>workItemNumber = 0</c>. Os da conversa do especialista entram como
    /// <c>source:"case"</c> (o áudio de quem fala fica de fora, como na conversa). Mais novos primeiro.
    /// </summary>
    public static List<WorkItemFileResponse> BuildForCard(IReadOnlyList<WorkItem> items, IEnumerable<WorkItemFile> own,
        IEnumerable<ExecutionArtifact> planArtifacts, IEnumerable<CaseFile>? caseFiles = null)
    {
        var ownByItem = own.ToLookup(f => f.WorkItemId);
        var plans = planArtifacts.ToList();
        var ownerOfPlan = new Dictionary<Guid, WorkItem>();
        foreach (var item in items.OrderBy(i => i.Number))
            if (item.PlanId is { } planId)
                ownerOfPlan.TryAdd(planId, item);

        var list = new List<WorkItemFileResponse>();
        foreach (var item in items.OrderBy(i => i.Number))
        {
            var mine = ownerOfPlan.Where(kv => kv.Value.Id == item.Id).Select(kv => kv.Key).ToHashSet();
            list.AddRange(WorkItemFileListing.Build(item.Number, ownByItem[item.Id],
                plans.Where(a => mine.Contains(a.PlanId)), TaskByStep(item)));
        }
        list.AddRange(plans.Where(a => !ownerOfPlan.ContainsKey(a.PlanId))
            .Select(a => WorkItemFileResponse.FromPlan(a, 0, null)));
        list.AddRange(CaseFile.Listed(caseFiles ?? []).Select(WorkItemFileResponse.FromCase));
        return list.OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
    }
}
