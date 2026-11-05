using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application.Contracts;

/// <summary>0085 r5: arquivo enviado para um item (tela, skill ou MCP). <paramref name="TaskNumber"/> = sub-tarefa do item.</summary>
public record WorkItemFileUpload(string FileName, string? Kind, long? TaskNumber, string ContentType, byte[] Data);

/// <summary>0085 r5: conteúdo de um arquivo do item.</summary>
public record WorkItemFileData(WorkItemFileResponse File, byte[] Data);

/// <summary>
/// 0085 r5 (1.2): seção "Arquivos" do item de trabalho (<c>GET/POST WorkItems/{n}/files</c>, MCP
/// <c>prmake_work_item_file(s)</c>). A lista junta os arquivos do item e os do plano ligado (<c>source:"plan"</c>).
/// </summary>
public interface IWorkItemFileApplication
{
    /// <summary>Arquivos do item + os do plano ligado (sem conteúdo).</summary>
    Task<IReadOnlyList<WorkItemFileResponse>> ListAsync(long number, CancellationToken ct);
    /// <summary>
    /// 0087: todos os arquivos do card — os dos itens, os dos planos ligados a eles e os de planos sem item (versões antigas,
    /// <c>workItemNumber = 0</c>). Sem conteúdo; cada um traz <c>contentUrl</c>.
    /// </summary>
    Task<IReadOnlyList<WorkItemFileResponse>> ListByCardAsync(string cardNumber, CancellationToken ct);
    /// <summary>Conteúdo de um arquivo do próprio item (os do plano saem pela rota do plano — <c>contentUrl</c>).</summary>
    Task<WorkItemFileData> GetContentAsync(long number, Guid fileId, CancellationToken ct);
    /// <summary>Grava (mesmo tipo + nome substitui). Avisa a tela (<c>workItemsUpdated</c> action <c>file</c>).</summary>
    Task<WorkItemFileResponse> UploadAsync(long number, WorkItemFileUpload upload, ExecutionActor actor, CancellationToken ct);
    /// <summary>
    /// 0089: importa (idempotente) para os arquivos do item as evidências do card no DevOps — anexos e imagens coladas na
    /// descrição, nos campos e na discussion —, mesmo com o card sem plano. Cada uma vira um anexo <c>ev-*</c> do item.
    /// </summary>
    Task<WorkItemEvidenceResponse> ImportEvidenceAsync(long number, ExecutionActor actor, CancellationToken ct);
    /// <summary>Card do item (para conferir o card no MCP e no especialista). Item inexistente → 404.</summary>
    Task<string> CardOfAsync(long number, CancellationToken ct);
}

/// <summary>0085 r5: arquivos do item (mesmo contexto do plano).</summary>
public interface IWorkItemFileRepository
{
    /// <summary>Item com sub-tarefas (sem rastrear).</summary>
    Task<WorkItem?> GetItemAsync(long number, CancellationToken ct);
    /// <summary>Arquivos do item, sem conteúdo.</summary>
    Task<List<WorkItemFile>> ListAsync(Guid workItemId, CancellationToken ct);
    /// <summary>Arquivo do item (sem conteúdo).</summary>
    Task<WorkItemFile?> GetAsync(Guid workItemId, Guid fileId, CancellationToken ct);
    /// <summary>Arquivo do item pelo tipo + nome, rastreado e com conteúdo (para substituir).</summary>
    Task<WorkItemFile?> FindForUpdateAsync(Guid workItemId, string kind, string name, CancellationToken ct);
    Task<byte[]?> GetContentAsync(Guid fileId, CancellationToken ct);
    Task<int> GetMaxNumberAsync(Guid workItemId, CancellationToken ct);
    Task<long> GetSizeAsync(Guid workItemId, Guid? excludingFileId, CancellationToken ct);
    /// <summary>Arquivos do plano (artefatos sem conteúdo).</summary>
    Task<List<ExecutionArtifact>> GetPlanArtifactsAsync(Guid planId, CancellationToken ct);
    /// <summary>0087: itens do card com sub-tarefas (sem rastrear), em ordem de número.</summary>
    Task<List<WorkItem>> GetItemsByCardAsync(string cardNumber, CancellationToken ct);
    /// <summary>0087: arquivos dos itens informados, sem conteúdo (uma leitura só).</summary>
    Task<List<WorkItemFile>> ListByItemsAsync(IReadOnlyCollection<Guid> workItemIds, CancellationToken ct);
    /// <summary>0087: arquivos de todos os planos do card (sem anexo de comentário nem evidência), sem conteúdo.</summary>
    Task<List<ExecutionArtifact>> GetCardPlanArtifactsAsync(string cardNumber, CancellationToken ct);
    /// <summary>Fase do plano (analysis|correction); null = não existe.</summary>
    Task<string?> GetPlanPhaseAsync(Guid planId, CancellationToken ct);
    void Add(WorkItemFile file);
    void AddLog(WorkItemLog log);
    /// <summary>Salva. Número repetido no item (corrida) → <see cref="WorkItemFileNumberConflictException"/>.</summary>
    Task SaveChangesAsync(CancellationToken ct);
    void ClearTracking();
}

/// <summary>0085 r5: dois arquivos gravados ao mesmo tempo pegaram o mesmo número/nome — tenta de novo.</summary>
public class WorkItemFileNumberConflictException(Exception inner) : Exception("Arquivo do item gravado ao mesmo tempo por outra ação.", inner);
