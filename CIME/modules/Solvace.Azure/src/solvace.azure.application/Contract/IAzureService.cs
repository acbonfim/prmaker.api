using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using solvace.azure.domain.Models;
using solvace.azure.domain.Requests;

namespace solvace.azure.application.Contract;

public interface IAzureService
{
    Task<AzureWorkItem?> GetCardAsync(string id, CancellationToken cancellationToken = default);
    Task<AzureCardFullResponse?> GetCardFullAsync(string id, CancellationToken cancellationToken = default);
    Task<AzureWorkItem?> UpdateRootCauseAsync(string id, UpdateRootCauseRequest bodyRaw, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aplica um JSON Patch de campos no work item (uma revisão só) e devolve o rev novo.
    /// Erro do DevOps: <see cref="solvace.azure.domain.Exceptions.DevOpsActionException"/> (502) com a mensagem dele.
    /// </summary>
    Task<long> PatchFieldsAsync(string id, IReadOnlyDictionary<string, object> fields, CancellationToken cancellationToken = default);

    /// <summary>0085: estados válidos de um tipo de work item (Bug, User Story...) no projeto, na ordem do DevOps.</summary>
    Task<IReadOnlyList<string>> GetWorkItemTypeStatesAsync(string workItemType, CancellationToken cancellationToken = default);

    /// <summary>0085 r3: estados do tipo com a categoria (Proposed/InProgress/Resolved/Completed/Removed), na ordem do DevOps. Cache curto por tipo.</summary>
    Task<IReadOnlyList<WorkItemTypeState>> GetWorkItemTypeStateInfosAsync(string workItemType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cria (commentId null) ou atualiza um comentário HTML na discussion do card e devolve o id.
    /// Comentário apagado no DevOps (404 ao atualizar) = cria outro.
    /// </summary>
    Task<int> UpsertCommentAsync(string id, string html, int? commentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sobe um arquivo como anexo do DevOps (sem vincular a um card) e devolve a URL — vai num &lt;img&gt; do comentário,
    /// como faz quem cola um print na discussion. Erro do DevOps: <see cref="solvace.azure.domain.Exceptions.DevOpsActionException"/> (502).
    /// </summary>
    Task<string> UploadAttachmentAsync(string fileName, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 0076: evidências do card — arquivos anexados e imagens/arquivos colados nos campos de texto e na discussion,
    /// sem repetir. Erro do DevOps: <see cref="solvace.azure.domain.Exceptions.DevOpsActionException"/> com a mensagem dele.
    /// </summary>
    Task<IReadOnlyList<AzureCardEvidence>> GetCardEvidenceAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 0076: baixa um anexo do DevOps (só da organização configurada). Maior que <paramref name="maxBytes"/> = TooLarge,
    /// sem o conteúdo.
    /// </summary>
    Task<AzureAttachmentDownload> DownloadAttachmentAsync(string url, long maxBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids dos work items que casam com a consulta WIQL (0039: regra automática da fila), do projeto configurado, no
    /// máximo <paramref name="top"/>. Erro do DevOps vira exceção com a mensagem dele.
    /// </summary>
    Task<IReadOnlyList<int>> QueryWorkItemIdsAsync(string wiql, int top, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumo de vários cards numa chamada só (<c>workitemsbatch</c>, 0051): cards inexistentes ou sem acesso ficam de
    /// fora. No máximo 200 ids.
    /// </summary>
    Task<IReadOnlyList<AzureCardSummaryResponse>> GetCardsSummaryAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Nomes dos campos do work item conforme o "AzureDevOps Configurations" (feature 0030).</summary>
    Task<solvace.azure.domain.Options.AzureDevOpsFieldNames> GetFieldNamesAsync(CancellationToken cancellationToken = default);
}