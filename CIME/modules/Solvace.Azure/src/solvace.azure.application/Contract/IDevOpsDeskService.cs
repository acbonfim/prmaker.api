using solvace.azure.domain.Models;

namespace solvace.azure.application.Contract;

/// <summary>
/// 0099 (Mesa de gestão): o Azure DevOps pela integração pessoal de quem age ("AzureDevOps Configurations") — metadados
/// (tipos, campos com picklist, tags, áreas/iterações, pessoas, times, boards), o board "igual ao DevOps" (colunas, lanes,
/// mapeamento coluna→estado, campo de ordem), consultas e criar/editar/mover/comentar cards. Erros: <see
/// cref="solvace.azure.domain.Exceptions.DevOpsDeskException"/> com <c>{code, message}</c> (a mensagem do DevOps quando ele
/// recusou); sem integração pessoal: <c>PersonalIntegrationRequiredException</c> (403 pelo filtro global).
/// </summary>
public interface IDevOpsDeskService
{
    Task<IReadOnlyList<DevOpsTypeInfo>> GetTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>Campos do tipo com tipo do valor, obrigatório, somente leitura e valores permitidos (picklist).</summary>
    Task<IReadOnlyList<DevOpsFieldInfo>> GetFieldsAsync(string workItemType, CancellationToken cancellationToken = default);

    /// <summary>Tags do projeto (filtro "contém", ignora caixa).</summary>
    Task<IReadOnlyList<string>> GetTagsAsync(string? q, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DevOpsNodeInfo>> GetAreasAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DevOpsNodeInfo>> GetIterationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Membros dos times do projeto (cache de 10 min), filtro "contém" no nome ou no login.</summary>
    Task<IReadOnlyList<DevOpsPersonInfo>> GetPeopleAsync(string? q, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DevOpsNamedRef>> GetTeamsAsync(CancellationToken cancellationToken = default);

    /// <summary>Boards do time (sem time: o time padrão do projeto).</summary>
    Task<IReadOnlyList<DevOpsNamedRef>> GetBoardsAsync(string? team, CancellationToken cancellationToken = default);

    /// <summary>
    /// Configuração do board (cache de 60 s, com <c>configHash</c>). Sem time: o padrão do projeto; sem board: o que tem
    /// <paramref name="workItemType"/> (quando informado) ou o do backlog de requisitos do time.
    /// </summary>
    Task<DevOpsBoardConfig> GetBoardAsync(string? team, string? board, string? workItemType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cartões do board. <paramref name="onlyIds"/> (escopo do épico) = só esses cards, nos tipos/estados do board; null =
    /// os do time (área do time), a coluna final só com os mais recentes.
    /// </summary>
    Task<DevOpsBoardItemsResponse> GetBoardItemsAsync(DevOpsBoardItemsRequest request, IReadOnlyCollection<int>? onlyIds,
        CancellationToken cancellationToken = default);

    /// <summary>Consulta por WIQL ou filtros (viram WIQL), com os campos escolhidos.</summary>
    Task<DevOpsQueryResponse> QueryAsync(DevOpsQueryRequest request, CancellationToken cancellationToken = default);

    Task<DevOpsCardDetail> GetCardAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Cria o card (campos, tags, pai) e, com coluna/lane, coloca no board (segunda revisão).</summary>
    Task<DevOpsCardWriteResult> CreateCardAsync(DevOpsCreateCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Edita campos/tags (tags lidas do card) e, com comentário, publica na discussion.</summary>
    Task<DevOpsCardWriteResult> UpdateCardAsync(int id, DevOpsUpdateCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Move de coluna (estado mapeado do tipo), done, lane e ordem (vizinhos).</summary>
    Task<DevOpsCardWriteResult> MoveCardAsync(int id, DevOpsMoveCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Comentário novo na discussion (markdown → HTML); devolve o id do comentário.</summary>
    Task<int> CommentAsync(int id, string markdown, CancellationToken cancellationToken = default);
}
