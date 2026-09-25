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

    /// <summary>
    /// Cria (commentId null) ou atualiza um comentário HTML na discussion do card e devolve o id.
    /// Comentário apagado no DevOps (404 ao atualizar) = cria outro.
    /// </summary>
    Task<int> UpsertCommentAsync(string id, string html, int? commentId, CancellationToken cancellationToken = default);
}