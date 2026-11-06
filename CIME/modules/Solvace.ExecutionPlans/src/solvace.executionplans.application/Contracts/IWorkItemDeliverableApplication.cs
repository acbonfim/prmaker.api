using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application.Contracts;

/// <summary>0091: código alterado enviado pela skill (<c>prmake-plan.sh deliver-code</c>) — o diff vem do worktree.</summary>
public record WorkDeliverableCodeUpload(string Repository, string Branch, string? Base, string? MergeBase, string? Head,
    IReadOnlyList<WorkDeliverableCommit> Commits, byte[] Patch);

public record WorkDeliverableCommit(string Sha, string? Title);

/// <summary>
/// 0091: o que o item e as sub-tarefas geraram (<c>GET WorkItems/{n}/deliverables</c>, <c>deliverables</c> do GET do
/// item, MCP <c>prmake_work_deliverable</c>). Gravar pela sub-tarefa informada, pela etapa do plano (sub-tarefa espelho)
/// ou — nos ganchos do servidor (abrir PR, DevOps, Timeline) — pela sub-tarefa em andamento no card.
/// </summary>
public interface IWorkItemDeliverableApplication
{
    /// <summary>Gravadas + deduzidas (output/links, links da etapa do plano, PRs do card).</summary>
    Task<List<WorkDeliverableResponse>> ListAsync(long number, CancellationToken ct);
    Task<List<WorkDeliverableResponse>> ListAsync(WorkItem item, CancellationToken ct);
    Task<WorkDeliverableResponse> RecordAsync(long taskNumber, WorkDeliverableInput input, ExecutionActor actor, CancellationToken ct);
    Task<WorkDeliverableResponse> RecordForStepAsync(Guid planId, string stepKey, WorkDeliverableInput input, ExecutionActor actor, CancellationToken ct);
    Task<WorkDeliverableResponse> RecordCodeAsync(long taskNumber, WorkDeliverableCodeUpload upload, ExecutionActor actor, CancellationToken ct);
    Task<WorkDeliverableResponse> RecordCodeForStepAsync(Guid planId, string stepKey, WorkDeliverableCodeUpload upload, ExecutionActor actor, CancellationToken ct);
    /// <summary>
    /// Gancho do servidor: grava na sub-tarefa informada (do card) ou na em andamento do card (com várias, a de tipo
    /// compatível); sem nenhuma, fica só no card. Nunca lança — o que gerou a entrega já aconteceu.
    /// </summary>
    Task<WorkDeliverableResponse?> RecordAutoAsync(string cardNumber, long? taskNumber, WorkDeliverableInput input, ExecutionActor actor, CancellationToken ct);
    /// <summary>Diff da entrega: a fotografia gravada ou o compare ao vivo no GitHub. Sem diff → 404.</summary>
    Task<WorkDeliverableDiffResponse> DiffAsync(long number, string deliverableId, CancellationToken ct);
    /// <summary>Etapa do plano sem código registrado (aviso do <c>prmake_advance</c>); null = tem ou não se aplica.</summary>
    Task<string?> MissingCodeWarningAsync(Guid planId, string stepKey, CancellationToken ct);
}

/// <summary>0091: o que vem de fora do módulo — PRs do card (do banco, sem ir ao GitHub) e o compare do GitHub.</summary>
public interface IWorkDeliverableSources
{
    Task<IReadOnlyList<DeliverableCardPr>> ListCardPullRequestsAsync(string cardNumber, CancellationToken ct);
    /// <summary>Compare <c>base...head</c> no repositório (arquivos com patch). Erro do GitHub → <c>Error</c>.</summary>
    Task<WorkDeliverableDiffResponse> CompareAsync(string repository, string @base, string head, CancellationToken ct);
}

/// <summary>0091: entregas (mesmo contexto do plano).</summary>
public interface IWorkItemDeliverableRepository
{
    /// <summary>Item com sub-tarefas (sem rastrear).</summary>
    Task<WorkItem?> GetItemAsync(long number, CancellationToken ct);
    /// <summary>Item dono da sub-tarefa, com sub-tarefas (sem rastrear).</summary>
    Task<WorkItem?> GetItemByTaskAsync(long taskNumber, CancellationToken ct);
    /// <summary>Itens do plano com sub-tarefas (sem rastrear).</summary>
    Task<List<WorkItem>> GetItemsByPlanAsync(Guid planId, CancellationToken ct);
    /// <summary>Itens do card com sub-tarefa em andamento (sem rastrear).</summary>
    Task<List<WorkItem>> GetItemsWithRunningTaskAsync(string cardNumber, CancellationToken ct);
    /// <summary>Entregas do item, sem conteúdo.</summary>
    Task<List<WorkItemDeliverable>> ListAsync(Guid workItemId, CancellationToken ct);
    /// <summary>Entrega pela chave na sub-tarefa (rastreada, com conteúdo).</summary>
    Task<WorkItemDeliverable?> FindForUpdateAsync(string cardNumber, long? taskNumber, string key, CancellationToken ct);
    Task<WorkItemDeliverable?> GetAsync(Guid workItemId, Guid id, CancellationToken ct);
    Task<byte[]?> GetPatchAsync(Guid id, CancellationToken ct);
    Task<List<ExecutionLink>> GetPlanLinksAsync(Guid planId, CancellationToken ct);
    /// <summary>Fase do plano (analysis|correction); null = não existe.</summary>
    Task<string?> GetPlanPhaseAsync(Guid planId, CancellationToken ct);
    void Add(WorkItemDeliverable deliverable);
    void AddLog(WorkItemLog log);
    /// <summary>Salva. Mesma chave gravada ao mesmo tempo → <see cref="WorkItemDeliverableConflictException"/>.</summary>
    Task SaveChangesAsync(CancellationToken ct);
    void ClearTracking();
}

public class WorkItemDeliverableConflictException(Exception inner) : Exception("Entrega gravada ao mesmo tempo por outra ação.", inner);
